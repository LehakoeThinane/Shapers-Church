namespace Shapers.Documents.Contracts;

public static class DocumentsPermissions
{
    /// <summary>Open the libraries of this scope and download their files.</summary>
    public const string View = "documents.view";

    /// <summary>Upload, rename, move and delete files and folders in the libraries of this scope.</summary>
    public const string Edit = "documents.edit";

    /// <summary>Create libraries, make them private and choose who's in them, and empty the recycle bin.</summary>
    public const string Manage = "documents.manage";
}
