namespace DictaClone.Speech;

public enum ModelDownloadStage
{
    Checking,
    Downloading,
    Verifying,
    Ready,
}

public sealed class ModelDownloadProgressEventArgs : EventArgs
{
    public ModelDownloadProgressEventArgs(
        string modelName,
        ModelDownloadStage stage,
        long bytesReceived,
        long totalBytes)
    {
        ModelName = modelName;
        Stage = stage;
        BytesReceived = bytesReceived;
        TotalBytes = totalBytes;
    }

    public string ModelName { get; }

    public ModelDownloadStage Stage { get; }

    public long BytesReceived { get; }

    public long TotalBytes { get; }

    public double Fraction => TotalBytes <= 0
        ? 0
        : Math.Clamp(BytesReceived / (double)TotalBytes, 0, 1);
}

public sealed record WhisperModelLocation(
    WhisperModelDefinition Model,
    string Path,
    bool ReusedExistingFile);

public sealed class ModelIntegrityException : IOException
{
    public ModelIntegrityException(string message)
        : base(message)
    {
    }
}
