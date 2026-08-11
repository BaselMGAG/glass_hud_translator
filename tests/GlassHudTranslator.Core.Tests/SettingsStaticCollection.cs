using Xunit;

namespace GlassHudTranslator.Core.Tests;

/// <summary>
/// Serialises every test class that touches <see cref="Core.Config.AppSettings"/>' global state.
///
/// <para>
/// <c>AppSettings.SafeMode</c> is a static, deliberately — it has to be set in <c>Program.Main</c>
/// before any settings read, so there is nowhere else for it to live. xUnit runs test CLASSES in
/// parallel, so while <c>SafeModeTests</c> holds it true, any other class calling
/// <c>AppSettings.Load</c> concurrently is short-circuited to defaults and sees a settings file it
/// wrote come back empty.
/// </para>
///
/// <para>
/// This had been latent since safe mode shipped and surfaced the first time new test classes
/// changed the scheduling enough to overlap the two — which is the worst way for it to appear,
/// because it looks exactly like whatever change happened to be in flight. A shared collection is
/// what xUnit provides for "these classes may not run at the same time"; the alternative, making
/// the flag an instance, would mean the production code carrying a seam that exists only for tests.
/// </para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SettingsStaticCollection
{
    public const string Name = "AppSettings global state";
}
