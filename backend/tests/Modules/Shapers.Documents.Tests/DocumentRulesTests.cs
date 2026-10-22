using Shapers.Documents.Domain;

namespace Shapers.Documents.Tests;

public sealed class DocumentRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Sunday run sheet.pdf", true)]
    [InlineData("  Budget 2027.xlsx  ", true)]
    [InlineData("", false)]
    [InlineData("..", false)]
    [InlineData("a/b.pdf", false)]
    [InlineData("what?.docx", false)]
    public void Names_are_trimmed_and_can_not_hold_path_characters(string name, bool valid)
    {
        if (valid)
        {
            Assert.Equal(name.Trim(), DocumentNames.Check(name, "file"));
        }
        else
        {
            Assert.Throws<DomainRuleException>(() => DocumentNames.Check(name, "file"));
        }
    }

    [Fact]
    public void Each_upload_adds_a_version_and_the_newest_is_current()
    {
        var doc = Document.Create(Guid.NewGuid(), null, "Rota.docx", Now);
        doc.AddVersion(doc.NextStorageKey("Rota.docx"), "application/msword", 10, null, "Thabo", Now);
        doc.AddVersion(doc.NextStorageKey("Rota.docx"), "application/msword", 20, null, "Lerato", Now.AddHours(1));

        Assert.Equal(2, doc.Current.Number);
        Assert.Equal("Lerato", doc.Current.UploadedByName);
        Assert.EndsWith(".docx", doc.Current.StorageKey, StringComparison.Ordinal);
        Assert.Throws<DomainRuleException>(() => doc.AddVersion("k", "text/plain", Document.MaxBytes + 1, null, "Thabo", Now));
    }

    [Fact]
    public void Old_versions_beyond_the_limit_are_let_go_but_a_shared_file_is_kept()
    {
        var doc = Document.Create(Guid.NewGuid(), null, "Notes.txt", Now);
        doc.AddVersion("v1", "text/plain", 1, null, "A", Now);
        doc.RestoreVersion(1, null, "A", Now); // Version 2 shares v1's file.

        var dropped = new List<string>();
        for (var i = 0; i < Document.MaxVersions; i++)
        {
            dropped.AddRange(doc.AddVersion($"file-{i}", "text/plain", 1, null, "A", Now));
        }

        Assert.Equal(Document.MaxVersions, doc.Versions.Count);
        Assert.Contains("v1", dropped); // Both versions using v1 are gone, so its file can go.
        Assert.Single(dropped, "v1");
    }

    [Fact]
    public void Restoring_a_version_makes_it_current_and_keeps_the_history()
    {
        var doc = Document.Create(Guid.NewGuid(), null, "Policy.pdf", Now);
        doc.AddVersion("one", "application/pdf", 1, null, "A", Now);
        doc.AddVersion("two", "application/pdf", 2, null, "B", Now);

        doc.RestoreVersion(1, null, "C", Now.AddDays(1));

        Assert.Equal(3, doc.Versions.Count);
        Assert.Equal("one", doc.Current.StorageKey);
        Assert.Equal("C", doc.Current.UploadedByName);
    }

    [Fact]
    public void Erasing_an_uploader_keeps_the_file_without_their_name()
    {
        var person = Guid.NewGuid();
        var doc = Document.Create(Guid.NewGuid(), null, "Minutes.docx", Now);
        doc.AddVersion("k", "application/msword", 1, person, "Thabo Mokoena", Now);

        Assert.True(doc.ForgetUploader(person));
        Assert.Equal("Former member", doc.Current.UploadedByName);
        Assert.Null(doc.Current.UploadedByPersonId);
    }

    [Fact]
    public void A_private_library_keeps_one_entry_per_person()
    {
        var library = Library.Create("Leadership", ScopePath.Parse("shapers"), isPrivate: true, Now);
        var person = Guid.NewGuid();
        library.SetMembers([new LibraryMember(person, false), new LibraryMember(person, true)], Now);

        var member = Assert.Single(library.Members);
        Assert.True(member.CanEdit);
    }
}
