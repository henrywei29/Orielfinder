using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Span.Models;
using Span.Services;

namespace Span.ViewModels;

public partial class ExplorerViewModel
{
    private long _colorLabelRequest;
    private FolderViewModel? _colorLabelFolder;
    private FolderTagColor _colorLabel;
    public bool IsColorLabelCategory => _colorLabelFolder != null && Columns.Count == 1
        && ReferenceEquals(Columns[0], _colorLabelFolder);

    public Task RefreshColorLabelCategoryAsync() => IsColorLabelCategory
        ? ShowColorLabelAsync(_colorLabel) : Task.CompletedTask;

    public async Task ShowColorLabelAsync(FolderTagColor color)
    {
        var tags = App.Current.Services.GetRequiredService<FolderTagService>();
        bool showTags = App.Current.Services.GetRequiredService<SettingsService>().FolderTagsEnabled;
        bool refresh = IsColorLabelCategory && _colorLabel == color;
        var selectedPaths = refresh ? _colorLabelFolder!.Children.Where(item => item.IsSelected)
            .Select(item => item.Path).ToHashSet(StringComparer.OrdinalIgnoreCase) : new HashSet<string>();
        if (!refresh)
        {
            PushToHistory("labels://" + color);
            CancelRecursiveSearchInternal(restoreColumns: false);
            foreach (var column in Columns)
            {
                column.PropertyChanged -= FolderVm_PropertyChanged;
                column.LoadError -= OnColumnLoadError;
                column.CancelLoading();
            }
            Columns.Clear();
            string name = tags.GetName(color);
            if (string.IsNullOrEmpty(name)) name = LocalizationService.L("Tag_" + color);
            _colorLabelFolder = new FolderViewModel(new FolderItem { Name = name, Path = "labels://" + color }, _fileService);
            _colorLabelFolder.MarkAsManuallyPopulated();
            _colorLabel = color;
            AddColumn(_colorLabelFolder);
            CurrentPath = _colorLabelFolder.Path;
            SelectedFile = null;
        }
        var target = _colorLabelFolder!;
        long request = ++_colorLabelRequest;
        // Read only the saved index. Never walk directories to discover labels.
        var paths = tags.GetTaggedPaths(color);
        var items = await Task.Run(() =>
        {
            var result = new List<FileSystemViewModel>();
            foreach (string path in paths)
            {
                try
                {
                    FileSystemViewModel item;
                    if (Directory.Exists(path))
                    {
                        var info = new DirectoryInfo(path);
                        item = new FolderViewModel(new FolderItem { Name = info.Name, Path = path,
                            IsHidden = info.Attributes.HasFlag(FileAttributes.Hidden) }, _fileService);
                    }
                    else if (File.Exists(path))
                    {
                        var info = new FileInfo(path);
                        item = new FileViewModel(new FileItem { Name = info.Name, Path = path, Size = info.Length,
                            DateModified = info.LastWriteTime, FileType = info.Extension,
                            IsHidden = info.Attributes.HasFlag(FileAttributes.Hidden) });
                    }
                    else continue; // Missing/moved external files are not displayed as broken entries.
                    item.LocationPath = Path.GetDirectoryName(path) ?? path;
                    item.TagColor = showTags ? color : FolderTagColor.None;
                    item.TagInjected = true;
                    result.Add(item);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return result;
        });
        if (request != _colorLabelRequest || !IsColorLabelCategory || !ReferenceEquals(target, _colorLabelFolder)) return;
        foreach (var item in items) item.IsSelected = selectedPaths.Contains(item.Path);
        target.Children = new System.Collections.ObjectModel.ObservableCollection<FileSystemViewModel>(items);
        NotifyCurrentItemsChanged();
    }
}
