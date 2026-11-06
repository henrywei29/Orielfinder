using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using OrielFolder.Helpers;
using OrielFolder.Models;
using OrielFolder.ViewModels;

namespace OrielFolder.Services
{
    /// <summary>
    /// BFS 방식의 재귀 검색 서비스.
    /// 백그라운드 스레드에서 파일 시스템을 순회하고, Channel을 통해 배치 결과를 UI로 전달.
    /// </summary>
    public class RecursiveSearchService
    {
        public class SearchProgress
        {
            public int FilesFound { get; set; }
            public int FoldersScanned { get; set; }
        }

        public const int MaxResults = 10_000;
        private const int BatchSize = 50;

        private readonly FileSystemService _fileService;

        public RecursiveSearchService(FileSystemService fileService)
        {
            _fileService = fileService;
        }

        /// <summary>
        /// 백그라운드 스레드에서 BFS 검색을 실행하고, Channel을 통해 배치 결과를 전달.
        /// 반환된 ChannelReader에서 배치(List)를 읽어 UI에 추가하면 됨.
        /// </summary>
        public ChannelReader<List<FileSystemViewModel>> SearchInBackground(
            string rootPath,
            SearchQuery query,
            bool showHidden,
            IProgress<SearchProgress>? progress,
            CancellationToken ct)
        {
            var channel = Channel.CreateBounded<List<FileSystemViewModel>>(
                new BoundedChannelOptions(16)
                {
                    SingleWriter = true,
                    SingleReader = true,
                    FullMode = BoundedChannelFullMode.Wait
                });

            // 백그라운드 스레드에서 BFS 실행 (UI 스레드 블로킹 없음)
            _ = Task.Run(async () =>
            {
                try
                {
                    await ProduceResultsAsync(channel.Writer, rootPath, query, showHidden, progress, ct);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[RecursiveSearch] 예외: {ex.Message}");
                }
                finally
                {
                    channel.Writer.TryComplete();
                }
            }); // Always run finally, even if cancellation predates worker scheduling.

            return channel.Reader;
        }

        private async Task ProduceResultsAsync(
            ChannelWriter<List<FileSystemViewModel>> writer,
            string rootPath,
            SearchQuery query,
            bool showHidden,
            IProgress<SearchProgress>? progress,
            CancellationToken ct)
        {
            DebugLogger.Log($"[RecursiveSearch] 시작: rootPath={rootPath}, showHidden={showHidden}, query={query.NameFilter}");

            var queue = new Queue<string>();
            queue.Enqueue(rootPath);

            int filesFound = 0;
            int foldersScanned = 0;
            var batch = new List<FileSystemViewModel>(BatchSize);

            // IgnoreInaccessible: MoveNext()에서 접근 불가 항목을 건너뜀 (루프 전체 중단 방지)
            // AttributesToSkip: 표시 규칙(Helpers/FileVisibility)을 열거자 레벨에서 적용
            var enumOptions = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = false,
                AttributesToSkip = Helpers.FileVisibility.AttributesToSkip(showHidden)
            };

            while (queue.Count > 0 && !ct.IsCancellationRequested)
            {
                var currentDir = queue.Dequeue();
                foldersScanned++;

                filesFound = await CollectMatchesAsync(writer, currentDir, rootPath, query,
                    showHidden, enumOptions, queue, batch, filesFound, ct);

                // 배치가 차면 Channel로 전송
                if (batch.Count > 0)
                {
                    await FlushBatchAsync(writer, batch, ct);
                }

                // 진행 상황 보고 (폴더 단위)
                if (foldersScanned % 20 == 0)
                {
                    progress?.Report(new SearchProgress
                    {
                        FilesFound = filesFound,
                        FoldersScanned = foldersScanned
                    });
                }

                if (filesFound >= MaxResults) break;
            }

            // 남은 배치 전송
            if (batch.Count > 0 && !ct.IsCancellationRequested)
            {
                await writer.WriteAsync(batch, ct);
            }

            // 최종 진행 보고
            DebugLogger.Log($"[RecursiveSearch] 완료: {filesFound}개 발견, {foldersScanned}개 폴더 스캔");
            progress?.Report(new SearchProgress
            {
                FilesFound = filesFound,
                FoldersScanned = foldersScanned
            });
        }

        /// <summary>
        /// 단일 디렉토리에서 매칭 아이템을 수집하여 batch에 추가.
        /// 백그라운드 스레드에서 실행됨.
        /// EnumerationOptions.IgnoreInaccessible = true로 접근 불가 항목을 자동 건너뜀.
        /// </summary>
        private static async Task FlushBatchAsync(ChannelWriter<List<FileSystemViewModel>> writer,
            List<FileSystemViewModel> batch, CancellationToken ct)
        {
            // The consumer owns its copy; clearing the producer buffer cannot erase UI results.
            await writer.WriteAsync(new List<FileSystemViewModel>(batch), ct);
            batch.Clear();
        }

        private async Task<int> CollectMatchesAsync(
            ChannelWriter<List<FileSystemViewModel>> writer,
            string currentDir,
            string rootPath,
            SearchQuery query,
            bool showHidden,
            EnumerationOptions enumOptions,
            Queue<string> queue,
            List<FileSystemViewModel> batch,
            int filesFound,
            CancellationToken ct)
        {
            DirectoryInfo dirInfo;
            try
            {
                dirInfo = new DirectoryInfo(currentDir);
                if (!dirInfo.Exists) return filesFound;
            }
            catch
            {
                return filesFound;
            }

            // 하위 폴더를 큐에 추가 + 폴더 자체 검색
            try
            {
                foreach (var sub in dirInfo.EnumerateDirectories("*", enumOptions))
                {
                    if (ct.IsCancellationRequested || filesFound >= MaxResults) break;

                    try
                    {
                        // 큐 추가를 최우선 실행 (BFS 진행이 매칭보다 중요)
                        // List links as results, but do not follow junctions into cycles/outside root.
                        if ((sub.Attributes & FileAttributes.ReparsePoint) == 0 || sub.LinkTarget == null)
                            queue.Enqueue(sub.FullName);

                        var folderItem = new FolderItem
                        {
                            Name = sub.Name,
                            Path = sub.FullName,
                            DateModified = sub.LastWriteTime,
                            IsHidden = (sub.Attributes & FileAttributes.Hidden) != 0
                        };
                        var folderVm = new FolderViewModel(folderItem, _fileService);
                        folderVm.MarkAsManuallyPopulated();

                        if (SearchFilter.Matches(query, folderVm))
                        {
                            folderVm.LocationPath = GetRelativeParentPath(rootPath, sub.FullName);
                            filesFound++;
                            batch.Add(folderVm);
                            if (batch.Count >= BatchSize) await FlushBatchAsync(writer, batch, ct);
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        DebugLogger.Log($"[RecursiveSearch] 폴더 항목 처리 실패 ({sub.FullName}): {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                DebugLogger.Log($"[RecursiveSearch] 디렉토리 열거 실패 ({currentDir}): {ex.Message}");
            }

            // 파일 검색
            try
            {
                foreach (var f in dirInfo.EnumerateFiles("*", enumOptions))
                {
                    if (ct.IsCancellationRequested || filesFound >= MaxResults) break;

                    try
                    {
                        var fileItem = new FileItem
                        {
                            Name = f.Name,
                            Path = f.FullName,
                            Size = f.Length,
                            DateModified = f.LastWriteTime,
                            FileType = f.Extension,
                            IsHidden = (f.Attributes & FileAttributes.Hidden) != 0
                        };
                        var fileVm = new FileViewModel(fileItem);

                        if (SearchFilter.Matches(query, fileVm))
                        {
                            fileVm.LocationPath = GetRelativeParentPath(rootPath, f.FullName);
                            filesFound++;
                            batch.Add(fileVm);
                            if (batch.Count >= BatchSize) await FlushBatchAsync(writer, batch, ct);
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        DebugLogger.Log($"[RecursiveSearch] 파일 항목 처리 실패 ({f.FullName}): {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                DebugLogger.Log($"[RecursiveSearch] 파일 열거 실패 ({currentDir}): {ex.Message}");
            }
            return filesFound;
        }

        /// <summary>
        /// 검색 루트 기준 상대 부모 경로 계산.
        /// 예: rootPath="D:\Projects", itemPath="D:\Projects\src\main.cs" → "src"
        /// </summary>
        private static string GetRelativeParentPath(string rootPath, string itemPath)
        {
            var parentDir = Path.GetDirectoryName(itemPath);
            if (string.IsNullOrEmpty(parentDir)) return string.Empty;

            // 루트와 동일한 폴더에 있는 항목
            if (parentDir.Equals(rootPath, StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            // 상대 경로 추출
            var root = rootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (parentDir.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return parentDir.Substring(root.Length);

            return parentDir;
        }
    }
}
