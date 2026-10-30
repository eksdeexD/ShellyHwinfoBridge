using ShellyHwinfoBridge.Core.Presentation;

namespace ShellyHwinfoBridge.Theming;

/// <summary>Maps the presenters' <see cref="Tone"/> onto the UI's <see cref="UiTone"/> (one-to-one by name).</summary>
internal static class ToneExtensions
{
    public static UiTone ToUi(this Tone tone) => tone switch
    {
        Tone.Secondary => UiTone.Secondary,
        Tone.Disabled => UiTone.Disabled,
        Tone.Accent => UiTone.Accent,
        Tone.Success => UiTone.Success,
        Tone.Warning => UiTone.Warning,
        Tone.Error => UiTone.Error,
        _ => UiTone.Ink,
    };
}
