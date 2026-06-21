using System;

namespace Wasmtime;

/// <summary>
/// Represents the WIT <c>unit</c> placeholder used for a result arm that carries no payload
/// (e.g. the error arm of <c>result&lt;t&gt;</c> or both arms of a bare <c>result</c>).
/// </summary>
public readonly struct Unit
{
    /// <summary>The single <see cref="Unit"/> value.</summary>
    public static Unit Value => default;
}

/// <summary>
/// Represents a WIT <c>result&lt;ok, err&gt;</c> value: either an <c>ok</c> payload of type
/// <typeparamref name="TOk"/> or an <c>err</c> payload of type <typeparamref name="TErr"/>.
/// Arms without a payload use <see cref="Unit"/>.
/// </summary>
/// <typeparam name="TOk">The ok payload type.</typeparam>
/// <typeparam name="TErr">The err payload type.</typeparam>
public readonly struct Result<TOk, TErr>
{
    private readonly TOk _value;
    private readonly TErr _error;

    /// <summary>True when this is the <c>ok</c> arm.</summary>
    public bool IsOk { get; }

    private Result(bool isOk, TOk value, TErr error)
    {
        IsOk = isOk;
        _value = value;
        _error = error;
    }

    /// <summary>The <c>ok</c> payload. Throws if this is the <c>err</c> arm.</summary>
    public TOk Value => IsOk
        ? _value
        : throw new InvalidOperationException("Result is an 'err'; the 'ok' value is not available.");

    /// <summary>The <c>err</c> payload. Throws if this is the <c>ok</c> arm.</summary>
    public TErr Error => !IsOk
        ? _error
        : throw new InvalidOperationException("Result is an 'ok'; the 'err' value is not available.");

    /// <summary>Creates an <c>ok</c> result.</summary>
    public static Result<TOk, TErr> Ok(TOk value) => new(true, value, default!);

    /// <summary>Creates an <c>err</c> result.</summary>
    public static Result<TOk, TErr> Err(TErr error) => new(false, default!, error);

    /// <inheritdoc />
    public override string ToString() => IsOk ? $"Ok({_value})" : $"Err({_error})";
}
