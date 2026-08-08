using GlassHudTranslator.Core.Storage;
using GlassHudTranslator.Core.Text;
using GlassHudTranslator.Core.Translation;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

/// <summary>
/// The cache key is a frozen wire format, not an implementation detail.
///
/// <para>
/// Every shipped installation has a populated <c>translations</c> table keyed by the exact string
/// this class produces. Change the derivation and every one of those rows becomes unreachable —
/// silently, because a miss is indistinguishable from a line never seen before. The user's only
/// symptom is paying quota again for lines they already paid for, which is the specific failure
/// the whole normalisation design exists to prevent (brief §5).
/// </para>
///
/// <para>
/// So these tests are not testing behaviour so much as pinning a contract. If one of them fails,
/// the correct response is almost never "update the expected value" — it is either to revert the
/// change or to write a migration first. The one case where updating is right is a deliberate,
/// migrated key change, and then the golden vectors move in the same commit as the migration.
/// </para>
/// </summary>
public class CacheKeyTests
{
    // ── the frozen shape ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Golden vectors computed from the derivation as shipped in v0.4.2.
    ///
    /// <para>
    /// Hardcoded rather than computed, on purpose: a test that recomputes the expected value using
    /// the same code under test passes no matter what that code does. These are the actual hex
    /// strings sitting in users' databases.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("Come with me.", "msa", "e1caf459fb16df6b08e5a09fea213239036bf0cbd90d5637e40b6d9bc2a30323")]
    [InlineData("Come with me.", "eg", "d83b393d2f0567e6b91be8aeb48a8798064c16b159f412bc454b0e453274a837")]
    [InlineData("Come, the aether stirs.", "msa", "8e0f84c8c76486b3b25b6f527f8ccdbd46629c0499e7d801ab4348fc3b1fb235")]
    [InlineData("Y'shtola waits at the aetheryte.", "msa", "e046dc005b67906210f8cfb97611af8786ab0eaa3f777d83da0e453e7211b85d")]
    [InlineData("", "msa", "6854688d8a04bb17bf99baf66b46e2e9a4b28a4f02c2a25c5d6fcad5aca9d0d9")]
    public void KeyShapeIsFrozenForShippedCaches(string body, string register, string expected)
    {
        Assert.Equal(expected, CacheKey.For(body, register));
    }

    [Fact]
    public void TheEnumOverloadAgreesWithTheStringOverload()
    {
        // Two entry points to one derivation. If they ever disagree, half the app's keys change and
        // the other half do not - which is worse than either changing.
        Assert.Equal(CacheKey.For("A line.", "msa"), CacheKey.For("A line.", ArabicRegister.ModernStandard));
        Assert.Equal(CacheKey.For("A line.", "eg"), CacheKey.For("A line.", ArabicRegister.Egyptian));
    }

    [Fact]
    public void RegisterIsPartOfTheKey()
    {
        // Removed once, in v0.1.0, and the symptom was that Egyptian appeared to do nothing at all:
        // switching dialect served the Modern Standard translation straight from cache and the
        // request never reached a model.
        Assert.NotEqual(
            CacheKey.For("Come with me.", ArabicRegister.ModernStandard),
            CacheKey.For("Come with me.", ArabicRegister.Egyptian));
    }

    [Fact]
    public void TheShippedRegisterTokensCannotSwallowPartOfTheBody()
    {
        // The newline separator alone does NOT make the encoding injective: register "msa\na" with
        // body "b" produces exactly the same canonical string as register "msa" with body "a\nb".
        // What actually protects the key is that only two register tokens are ever produced, and
        // neither contains a newline. That is the property worth pinning - if a third register is
        // ever added, or the public string overload is called with something else, this is the
        // assumption that quietly breaks.
        foreach (var register in new[] { ArabicRegister.ModernStandard, ArabicRegister.Egyptian })
        {
            var token = CacheKey.TokenFor(register);

            Assert.DoesNotContain('\n', token);
            Assert.NotEmpty(token);
        }
    }

    // ── the invariant that makes a future key change survivable ───────────────────────────

    /// <summary>
    /// <c>translations.source</c> stores byte-for-byte the string that was hashed.
    ///
    /// <para>
    /// This is the single most valuable property in the schema and nothing tested it until now. It
    /// is what makes a future key change a migration rather than a data loss: every row can be
    /// rehashed from what it already stores. Break it — by storing the raw OCR, or the text with
    /// the speaker still attached, or a trimmed variant — and the only recovery is to discard every
    /// user's cache.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SourceColumnReproducesTheKeyItWasStoredUnder()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ghck-{Guid.NewGuid():N}.db");
        try
        {
            await using var db = await AppDatabase.OpenAsync(path, CancellationToken.None);
            var cache = new SqliteTranslationCache(db);

            string[] bodies =
            [
                "Come with me.",
                "Y'shtola waits at the aetheryte.",
                "  leading and trailing  ",
                "MiXeD CaSe LiNe",
                "سطر عربي في المصدر",
            ];

            foreach (var body in bodies)
            {
                var key = CacheKey.For(body, ArabicRegister.ModernStandard);
                await cache.PutAsync(
                    new CachedTranslation(key, body, "ترجمة", "stub", "stub", false,
                        DateTimeOffset.UtcNow, 0),
                    CancellationToken.None);
            }

            // The rehash a migration would perform, proved to land on the same key.
            foreach (var body in bodies)
            {
                var key = CacheKey.For(body, ArabicRegister.ModernStandard);
                var row = await cache.TryGetAsync(key, CancellationToken.None);

                Assert.NotNull(row);
                Assert.Equal(key, CacheKey.For(row.Source, ArabicRegister.ModernStandard));
            }
        }
        finally
        {
            SafeDelete(path);
        }
    }

    [Fact]
    public async Task APinnedCorrectionAlsoReproducesItsKey()
    {
        // Overrides are the highest-value rows in the table - a user typed them - and they are
        // written by a different method, on a different code path, from a different screen.
        var path = Path.Combine(Path.GetTempPath(), $"ghck-{Guid.NewGuid():N}.db");
        try
        {
            await using var db = await AppDatabase.OpenAsync(path, CancellationToken.None);
            var cache = new SqliteTranslationCache(db);

            const string body = "The Warrior of Light approaches.";
            var key = CacheKey.For(body, ArabicRegister.Egyptian);

            await cache.PutOverrideAsync(key, body, "محارب النور جاي", CancellationToken.None);

            var row = await cache.TryGetAsync(key, CancellationToken.None);
            Assert.NotNull(row);
            Assert.Equal(key, CacheKey.For(row.Source, ArabicRegister.Egyptian));
            Assert.True(row.IsOverride);
        }
        finally
        {
            SafeDelete(path);
        }
    }

    // ── normalisation feeds the key, so its output is frozen too ──────────────────────────

    /// <summary>
    /// The per-profile OCR corrections run <em>inside</em> normalisation, which means editing a
    /// game's <c>ocr-corrections.json</c> already invalidates every cached row whose text those
    /// rules touch. That is a real, undocumented invalidation vector; this test at least pins the
    /// shipped rules so it cannot happen by accident.
    /// </summary>
    [Fact]
    public void NormalisationIsStableForShippedRules()
    {
        var corrections = OcrCorrections.Load(
            Path.Combine(TestPaths.Profiles, "ffxiv", "ocr-corrections.json"));

        // Each pair is a real OCR failure mode that must keep collapsing onto one key: if these
        // diverge, the same line on screen starts costing two requests instead of one.
        (string Mangled, string Clean)[] pairs =
        [
            ("Y shtola", "Y'shtola"),
            ("Come  with   me.", "Come with me."),
            ("Come with me.▼", "Come with me."),
            ("“Come with me.”", "\"Come with me.\""),
            ("Come with me. ", "Come with me."),
            ("Come with me.  ", "Come with me."),
        ];

        foreach (var (mangled, clean) in pairs)
        {
            var a = TextNormalizer.Normalize(mangled, corrections);
            var b = TextNormalizer.Normalize(clean, corrections);

            Assert.Equal(CacheKey.For(a, ArabicRegister.ModernStandard),
                CacheKey.For(b, ArabicRegister.ModernStandard));
        }
    }

    /// <summary>
    /// A known divergence, pinned deliberately rather than fixed.
    ///
    /// <para>
    /// A single-character ellipsis and three dots hash differently, so a line Tesseract reads as
    /// <c>…</c> on one frame and <c>...</c> on another is paid for twice. That is precisely the
    /// quota-leak shape <c>CLAUDE.md</c> warns about, and <c>PROJECT_PLAN.md</c> §1.5 says the
    /// intent was to fold it — <c>UnifyPunctuation</c> never did.
    /// </para>
    ///
    /// <para>
    /// It is not fixed here on purpose. Folding <c>…</c> changes the key for every cached line
    /// containing one, which is a migration, and the migration machinery is being built in the same
    /// change as this test. It is a good first job for that ladder: small blast radius, and the
    /// <c>source</c> invariant above proves the rows can be rehashed rather than discarded. When
    /// someone fixes it, this test fails and tells them so.
    /// </para>
    /// </summary>
    [Fact]
    public void EllipsisVariantsStillFragmentTheCache()
    {
        var a = TextNormalizer.Normalize("Come with me…", OcrCorrections.Empty);
        var b = TextNormalizer.Normalize("Come with me...", OcrCorrections.Empty);

        Assert.NotEqual(CacheKey.For(a, ArabicRegister.ModernStandard),
            CacheKey.For(b, ArabicRegister.ModernStandard));
    }

    [Fact]
    public void NormalisationPreservesCaseForThePromptWhileTheKeyIgnoresIt()
    {
        // The split the whole design turns on: the model sees "Limsa Lominsa", the key sees
        // "limsa lominsa". Collapsing them in either direction is a regression - one costs
        // translation quality, the other costs quota.
        const string text = "Limsa Lominsa";
        var normalized = TextNormalizer.Normalize(text, OcrCorrections.Empty);

        Assert.Equal(text, normalized);
        Assert.Equal(
            CacheKey.For(normalized, ArabicRegister.ModernStandard),
            CacheKey.For("limsa lominsa", ArabicRegister.ModernStandard));
    }

    // ── the original behavioural tests, kept alongside the frozen shape ───────────────────

    [Fact]
    public void OcrVariantsOfTheSameLineCollapseToOneKey()
    {
        // The quota guard (brief 5). If these two produce different keys, the same line of
        // dialogue is paid for twice, and that - not session length - is what exhausts a daily
        // budget. This test is the reason the correction dictionary runs before hashing.
        var corrections = new OcrCorrections(new Dictionary<string, string> { ["Y shtola"] = "Y'shtola" });

        var clean = TextNormalizer.Normalize("Y shtola nods slowly.", corrections);
        var mangled = TextNormalizer.Normalize("Y’shtola   nods slowly. ▼", corrections);

        Assert.Equal(CacheKey.For(clean), CacheKey.For(mangled));
    }

    [Fact]
    public void CaseDoesNotAffectTheKey()
    {
        Assert.Equal(CacheKey.For("Come, the aether stirs."), CacheKey.For("come, THE aether STIRS."));
    }

    [Fact]
    public void DifferentLinesProduceDifferentKeys()
    {
        Assert.NotEqual(CacheKey.For("Come with me."), CacheKey.For("Come with us."));
    }

    [Fact]
    public void KeyIsLowercaseHexSha256()
    {
        var key = CacheKey.For("anything");

        Assert.Equal(64, key.Length);
        Assert.Matches("^[0-9a-f]{64}$", key);
    }

    private static void SafeDelete(string path)
    {
        foreach (var file in new[] { path, path + "-wal", path + "-shm" })
        {
            try
            {
                if (File.Exists(file)) File.Delete(file);
            }
            catch (IOException)
            {
                // The connection pool can still hold a handle briefly on some platforms. A leftover
                // temp file is not worth failing a test over.
            }
        }
    }
}
