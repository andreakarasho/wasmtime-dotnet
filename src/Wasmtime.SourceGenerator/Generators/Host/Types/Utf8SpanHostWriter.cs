using Wasmtime.SourceGenerator.Models;

namespace Wasmtime.SourceGenerator.Generators.Host;

/// <summary>
/// Host writer for WIT <c>string</c> parameters whose name ends in <c>-utf8</c>: the host
/// surface takes <c>ReadOnlySpan&lt;byte&gt;</c> borrowing the raw canonical-ABI UTF-8 bytes
/// (<see cref="global::Wasmtime.ComponentValue.ToUtf8Span"/>) instead of allocating a managed
/// string per call. Opt-in for hot per-frame byte channels; the span is only valid for the
/// duration of the host callback.
/// </summary>
public class Utf8SpanHostWriter() : TypeHostWriter(WitTypeKind.String)
{
    // No ComponentValue is created for the read direction, and the write direction
    // (host->guest call) re-encodes through a string below — nothing to dispose.
    public override bool MustBeDisposed => false;

    public override void WriteCSharpType(IndentedStringBuilder sb, ITypeContainerResolver resolver)
    {
        sb.Append("global::System.ReadOnlySpan<byte>");
    }

    public override void WriteValueGetter(IndentedStringBuilder sb, string paramName, string uniqueName, ITypeContainerResolver resolver)
    {
        sb.Append(paramName).Append(".ToUtf8Span()");
    }

    protected override void WriteCreateComponentValue(IndentedStringBuilder sb, string paramKey, ITypeContainerResolver resolver, bool externallyOwned)
    {
        // Host->guest direction (host calls a guest function that takes a -utf8 string):
        // the bytes are already UTF-8, round-trip through a string for the existing
        // CreateString path. Cold direction — correctness over speed.
        sb.Append("global::Wasmtime.ComponentValue.CreateString(global::System.Text.Encoding.UTF8.GetString(")
          .Append(paramKey).Append("), externallyOwned: ").Append(externallyOwned ? "true" : "false").Append(')');
    }
}
