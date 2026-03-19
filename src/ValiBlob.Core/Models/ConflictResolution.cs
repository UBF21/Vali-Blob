namespace ValiBlob.Core.Models;

public enum ConflictResolution
{
    /// <summary>Overwrite the existing file (default behavior).</summary>
    Overwrite = 0,

    /// <summary>Automatically rename to avoid conflict (e.g. file_1.txt, file_2.txt).</summary>
    Rename = 1,

    /// <summary>Fail the upload with a StorageValidationException if the file already exists.</summary>
    Fail = 2
}
