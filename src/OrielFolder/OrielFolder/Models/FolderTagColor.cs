namespace OrielFolder.Models;

/// <summary>
/// Issue #58: 폴더 컬러 태그. macOS Finder의 컬러 라벨에 대응한다.
/// Stored as enum names in OrielFolder's central folder-tags.json; preserve these names.
/// Legacy desktop.ini values are supported read-only.
/// </summary>
public enum FolderTagColor
{
    None = 0,
    Red,
    Orange,
    Yellow,
    Green,
    Blue,
    Purple,
    Gray,
}
