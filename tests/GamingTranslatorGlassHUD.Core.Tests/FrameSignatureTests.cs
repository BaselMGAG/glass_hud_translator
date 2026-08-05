using GamingTranslatorGlassHUD.Core.Capture;
using Xunit;

namespace GamingTranslatorGlassHUD.Core.Tests;

public class FrameSignatureTests
{
    private const int BoxWidth = 800;
    private const int BoxHeight = 300;
    private const double BoxOpacity = 0.72;

    /// <summary>
    /// A dialogue box over a scene, with "glyphs" as bright bars. <paramref name="scene"/> is what
    /// shows through the translucent box; <paramref name="wordCount"/> stands in for the text.
    /// </summary>
    private static Frame DialogueBox(Rgb scene, int wordCount)
    {
        var boxColour = FrameBuilder.Blend(Rgb.BoxDark, scene, BoxOpacity);
        var builder = new FrameBuilder(BoxWidth, BoxHeight, scene)
            .Rect(20, 20, BoxWidth - 40, BoxHeight - 40, boxColour);

        for (var i = 0; i < wordCount; i++)
        {
            var x = 60 + i % 6 * 120;
            var y = 70 + i / 6 * 60;
            builder.Rect(x, y, 90, 26, Rgb.TextWhite);
        }

        return builder.Build();
    }

    [Fact]
    public void IdenticalFrames_AreIdentical()
    {
        var a = FrameSignature.Compute(DialogueBox(Rgb.DarkScene, wordCount: 8));
        var b = FrameSignature.Compute(DialogueBox(Rgb.DarkScene, wordCount: 8));

        Assert.Equal(0, a.DifferenceCount(b));
        Assert.True(a.LooksIdenticalTo(b));
        Assert.Equal(a.Hash, b.Hash);
    }

    [Fact]
    public void AddingOneWord_IsDetectedAsAChange()
    {
        // The expensive direction to be wrong in: a missed change means a stale translation.
        var before = FrameSignature.Compute(DialogueBox(Rgb.DarkScene, wordCount: 8));
        var after = FrameSignature.Compute(DialogueBox(Rgb.DarkScene, wordCount: 9));

        Assert.False(after.LooksIdenticalTo(before));
    }

    [Fact]
    public void SceneBehindTranslucentBox_ChangingDoesNotCountAsTextChange()
    {
        // This is the case that rules out comparing raw grey levels: the player moves the camera
        // from a dark zone into a bright one and every pixel under the box shifts, but the
        // dialogue has not advanced. Binarising first is what makes this pass.
        var darkZone = FrameSignature.Compute(DialogueBox(Rgb.DarkScene, wordCount: 8));
        var brightZone = FrameSignature.Compute(DialogueBox(Rgb.BrightScene, wordCount: 8));

        Assert.True(brightZone.LooksIdenticalTo(darkZone),
            $"differed in {brightZone.DifferenceCount(darkZone)} cells");
    }

    [Fact]
    public void FirstFrame_CountsAsChanged()
    {
        var first = FrameSignature.Compute(DialogueBox(Rgb.DarkScene, wordCount: 4));

        Assert.False(first.LooksIdenticalTo(null));
    }

    [Fact]
    public void InkRatio_ReflectsHowMuchTextIsPresent()
    {
        var sparse = FrameSignature.Compute(DialogueBox(Rgb.DarkScene, wordCount: 2));
        var dense = FrameSignature.Compute(DialogueBox(Rgb.DarkScene, wordCount: 12));

        Assert.True(dense.InkRatio > sparse.InkRatio,
            $"dense={dense.InkRatio:P1} sparse={sparse.InkRatio:P1}");
    }

    [Fact]
    public void Signature_IsAlwaysTheDeclaredSize()
    {
        var signature = FrameSignature.Compute(DialogueBox(Rgb.DarkScene, wordCount: 6));

        Assert.InRange(signature.InkRatio, 0.0, 1.0);
        Assert.Equal(FrameSignature.CellCount, FrameSignature.Width * FrameSignature.Height);
        Assert.Equal(0, signature.DifferenceCount(signature));
    }
}
