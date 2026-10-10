using Psarc2Tab.Application;
using Psarc2Tab.Domain;
using Psarc2Tab.Tablature;

namespace Psarc2Tab.Application.Tests;

/// <summary>Tests application orchestration from selected arrangement to tablature.</summary>
public sealed class ExportArrangementUseCaseTests
{
    /// <summary>Imports the requested arrangement and writes the resulting track.</summary>
    [Fact]
    public void Execute_ImportedSong_WritesTabDocument()
    {
        var song = new Song("Test", "Artist", [new Track("Lead", new GuitarTuning([40]),
            [new NoteEvent(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(0.5), new StringNumber(1), new FretNumber(3))], [])]);
        var useCase = new ExportArrangementUseCase(new StubArrangementImporter(song), new TablatureTextWriter());
        using var archive = new MemoryStream([1]);
        using var output = new StringWriter();

        useCase.Execute(archive, 2, output);

        Assert.Equal("e|3|\n", output.ToString());
    }

    /// <summary>Provides a fixed song for application orchestration tests.</summary>
    private sealed class StubArrangementImporter : IArchiveArrangementImporter
    {
        /// <summary>The song returned by this importer.</summary>
        private readonly Song _song;

        /// <summary>Creates an importer that returns a predetermined song.</summary>
        /// <param name="song">The song to return.</param>
        public StubArrangementImporter(Song song) => _song = song;

        /// <inheritdoc />
        public Song Import(Stream archive, int arrangementNumber) => _song;
    }
}
