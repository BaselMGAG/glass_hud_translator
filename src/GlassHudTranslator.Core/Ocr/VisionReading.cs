using System.Text;
using System.Text.Json;
using GlassHudTranslator.Core.Glossary;

namespace GlassHudTranslator.Core.Ocr;

/// <summary>
/// One frame put to a vision model, together with everything already known about it.
/// </summary>
/// <param name="LocalReading">
/// What the local engine made of this crop. Empty when it could not read it at all.
///
/// <para>
/// <b>Sending this is the single most important choice in the design.</b> Asking a model to read a
/// crop from scratch is generation; asking it whether an existing reading is right is verification,
/// and verification is where a language model's habit of guessing from context stops being
/// dangerous. Measured work on multimodal confidence finds image-plus-OCR beats image-alone on both
/// accuracy and on how well the answer can be trusted — with the largest gains on exactly the small
/// cheap models a free-lane-first router reaches.
/// </para>
/// </param>
/// <param name="Vocabulary">
/// The game's proper nouns.
///
/// <para>
/// <b>This is the advantage this app has and comparable tools do not.</b> The documented weakness of
/// multimodal OCR is text that carries no meaning: accuracy falls by roughly 57 points on scrambled
/// or invented words, against about 5 for a supervised recogniser, because the model reads by
/// knowing what a word probably is. A game's invented vocabulary — Y'shtola, Limsa Lominsa,
/// linkpearl — is precisely that kind of text. We already keep a per-game list of those words for
/// the translation prompt. Handing it over turns the model's worst case into a lookup.
/// </para>
/// </param>
public sealed record VisionRequest(
    VisionImage Image,
    string LocalReading,
    IReadOnlyList<GlossaryTerm> Vocabulary,
    string GameName = "a video game",
    bool WantsUnderstudy = false);

/// <summary>What a vision model gave back, before anything has been decided about it.</summary>
public sealed record VisionAnswer(string Text, string? Arabic = null);

/// <summary>
/// A second reader. Separate from <see cref="IOcrEngine"/> because it cannot honour that contract:
/// it has no per-word geometry and no confidence of its own, and inventing either would poison the
/// health check and the region-quality report that read them.
/// </summary>
public interface IVisionReader
{
    string Name { get; }

    /// <summary>False when no key is configured, so the lane is skipped in silence.</summary>
    bool IsConfigured { get; }

    Task<VisionAnswer> ReadAsync(VisionRequest request, CancellationToken ct);
}

/// <summary>
/// What to say to a vision model, and how to read the answer.
///
/// <para>
/// Kept apart from the transport for the reason <c>PromptBuilder</c> is: the wording is the part
/// that decides whether the feature works, it changes far more often than the HTTP does, and it is
/// the only part that can be tested without a key.
/// </para>
/// </summary>
public static class VisionPrompt
{
    /// <summary>
    /// The instruction.
    ///
    /// <para>
    /// <b>Abstention is asked for in permission form, never in severe form</b>, and that is a
    /// measured distinction rather than a stylistic one. Strongly worded "refuse if you are not
    /// certain" instructions suppress correct answers on legible input — one published test saw a
    /// model's accuracy fall from 58% to 9% under one. This lane only ever sees marginal frames, so
    /// a severe prompt would decline most of the frames the feature exists to rescue.
    /// </para>
    ///
    /// <para>
    /// The sentinel matters for the same reason it does on the other side of the pipeline: it
    /// collapses "there is nothing here" and "I will not guess" onto one answer that is
    /// distinguishable from a failed call, which is the distinction
    /// <c>PipelineOutcome.Result is null</c> already draws for translations.
    /// </para>
    /// </summary>
    public static string System(VisionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var prompt = new StringBuilder();

        prompt.Append("You read text from a screenshot of ").Append(request.GameName).Append(". ");

        prompt.Append(request.LocalReading.Length > 0
            ? "Another program has already read this image and its attempt is given below. Your job "
              + "is to CORRECT it against what the image actually shows: keep every word it got "
              + "right, and change only what is genuinely wrong. Do not rewrite it into better "
              + "prose, and do not add anything that is not visible in the image. "
            : "Transcribe the text visible in this image exactly as it appears. ");

        prompt.Append("Reply with the text only — no explanation, no quotation marks, no markdown. ");

        // Permission, not command. See the remarks above.
        prompt.Append("If the image has no readable text in it, or you are not able to make it out, "
            + "reply with exactly ").Append(ReadingJudge.NothingToRead).Append(" and nothing else. ");

        if (request.Vocabulary.Count > 0)
        {
            prompt.Append("These names occur in this game and are spelled exactly like this — prefer "
                + "them wherever the image is consistent with them: ");
            prompt.AppendJoin(", ", request.Vocabulary.Select(t => t.En));
            prompt.Append(". ");
        }

        if (request.WantsUnderstudy)
        {
            prompt.Append("Reply as JSON: {\"text\": \"<what the image says>\", \"arabic\": "
                + "\"<that text translated into Modern Standard Arabic>\"}. Use ")
                  .Append(ReadingJudge.NothingToRead)
                  .Append(" as the value of \"text\" if there is nothing readable.");
        }

        return prompt.ToString();
    }

    /// <summary>The user turn: the previous reading, when there is one.</summary>
    public static string User(VisionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.LocalReading.Length > 0
            ? $"The other program read this as:\n{request.LocalReading}"
            : "What does this say?";
    }

    /// <summary>
    /// Turns whatever came back into an answer.
    ///
    /// <para>
    /// Tolerant on purpose, and in one direction only. Models wrap answers in markdown fences, add
    /// a leading "Sure," and return a bare string where JSON was asked for — none of which is worth
    /// failing a request over, because the fallback is not a better answer, it is no answer at all.
    /// What it must NOT do is repair the text itself: a reading that arrives mangled is a reading
    /// the agreement gate should get to see mangled.
    /// </para>
    /// </summary>
    public static VisionAnswer Parse(string? raw)
    {
        var body = StripFences(raw ?? "").Trim();
        if (body.Length == 0) return new VisionAnswer("");

        if (body.StartsWith('{'))
        {
            try
            {
                using var json = JsonDocument.Parse(body);
                var root = json.RootElement;

                var text = root.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
                var arabic = root.TryGetProperty("arabic", out var a) ? a.GetString() : null;

                return new VisionAnswer(text.Trim(), string.IsNullOrWhiteSpace(arabic) ? null : arabic.Trim());
            }
            catch (JsonException)
            {
                // Asked for JSON, got something shaped like it but broken. The text is still worth
                // having - falling through treats it as a plain reading, which the agreement gate
                // will reject if it really is nonsense.
            }
        }

        return new VisionAnswer(body);
    }

    /// <summary>Removes a ``` fence, which every model produces sooner or later whatever it is told.</summary>
    internal static string StripFences(string body)
    {
        var text = body.Trim();
        if (!text.StartsWith("```", StringComparison.Ordinal)) return text;

        var firstBreak = text.IndexOf('\n');
        if (firstBreak < 0) return text;

        var inner = text[(firstBreak + 1)..];
        var close = inner.LastIndexOf("```", StringComparison.Ordinal);

        return (close < 0 ? inner : inner[..close]).Trim();
    }
}
