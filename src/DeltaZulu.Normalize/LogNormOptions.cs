namespace DeltaZulu.Normalize;

/// <summary>
/// Context options, mirroring the LN_CTXOPT_* flags of the C library.
/// </summary>
[Flags]
public enum LogNormOptions : uint
{
    None = 0,

    /// <summary>Always add the original message to the output (not just on failure).</summary>
    AddOriginalMessage = 0x04,

    /// <summary>Add a mock-up of the matching rule to the metadata.</summary>
    AddRule = 0x08,

    /// <summary>Add the matching rule's location (file, line number) to the metadata.</summary>
    AddRuleLocation = 0x10,
}
