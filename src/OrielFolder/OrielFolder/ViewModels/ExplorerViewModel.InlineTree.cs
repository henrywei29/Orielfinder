using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OrielFolder.Helpers;

namespace OrielFolder.ViewModels;

public partial class ExplorerViewModel
{
    private InlineTreeState<FileSystemViewModel>? _inlineTree;
    public InlineTreeState<FileSystemViewModel> InlineTree => _inlineTree ??= new(
        item => item.Path,
        item => item is FolderViewModel && item.Name != "..",
        item => item is FolderViewModel folder ? folder.Children : Enumerable.Empty<FileSystemViewModel>(),
        async item =>
        {
            if (item is not FolderViewModel folder) return;
            if (folder.HasError) await folder.ReloadAsync();
            else await folder.EnsureChildrenLoadedAsync();
            if (folder.HasError) throw new IOException(folder.ErrorMessage);
        });

    public List<FileSystemViewModel> ProjectTreeItems(IEnumerable<FileSystemViewModel> roots,
        Func<IEnumerable<FileSystemViewModel>, IEnumerable<FileSystemViewModel>>? arrange = null)
    {
        InlineTree.SetRoot(CurrentPath);
        var rows = InlineTree.Project(roots, arrange);
        foreach (var row in rows)
        {
            row.Item.TreeDepth = row.Depth;
            row.Item.IsTreeExpanded = row.IsExpanded;
        }
        return rows.Select(row => row.Item).ToList();
    }
}
