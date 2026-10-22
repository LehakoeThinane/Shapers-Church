using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Shapers.Documents.Application;
using Shapers.Identity.Application;
using Shapers.People.Application;

namespace Shapers.IntegrationTests;

public sealed class DocumentsTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Staff_share_files_with_versions_a_recycle_bin_and_private_libraries()
    {
        var admin = await api.SignInAdminAsync();
        var worship = await (await admin.PostJsonAsync("/api/admin/documents/libraries", new SaveLibraryRequest("Worship team", null, false))).ReadAsync<LibraryDto>();
        var leadership = await (await admin.PostJsonAsync("/api/admin/documents/libraries", new SaveLibraryRequest("Leadership", null, true))).ReadAsync<LibraryDto>();
        var folder = await (await admin.PostJsonAsync("/api/admin/documents/folders", new CreateFolderRequest(worship.Id, null, "Rehearsals"))).ReadAsync<FolderDto>();

        // Uploading the same name again makes a new version rather than a second file.
        var first = await (await UploadAsync(admin, worship.Id, folder.Id, "Run sheet.txt", "first draft")).ReadAsync<DocumentDto>();
        var second = await (await UploadAsync(admin, worship.Id, folder.Id, "run sheet.txt", "final")).ReadAsync<DocumentDto>();
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(2, second.Version);

        var view = await (await admin.GetAsync($"/api/admin/documents/libraries/{worship.Id}/folder?folderId={folder.Id}")).ReadAsync<FolderViewDto>();
        Assert.Equal("Rehearsals", Assert.Single(view.Breadcrumb).Name);
        Assert.Single(view.Documents);

        // The current version opens in the browser; an earlier one can still be fetched.
        var opened = await admin.GetAsync($"/api/admin/documents/files/{first.Id}/content");
        Assert.Equal("final", await opened.Content.ReadAsStringAsync());
        Assert.Equal("inline", opened.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("first draft", await (await admin.GetAsync($"/api/admin/documents/files/{first.Id}/content?version=1")).Content.ReadAsStringAsync());

        // The editor saves its copy as the document's next version, whatever name the browser sends.
        var detail = await (await admin.GetAsync($"/api/admin/documents/files/{first.Id}")).ReadAsync<DocumentDetailDto>();
        Assert.Equal(worship.Id, detail.LibraryId);
        Assert.Equal(LibraryAccess.Manage, detail.Access);
        var edited = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes("edited")), "file", "anything.docx" } };
        var third = await (await admin.PostAsync($"/api/admin/documents/files/{first.Id}/versions", edited)).ReadAsync<DocumentDto>();
        Assert.Equal(3, third.Version);
        Assert.Equal("Run sheet.txt", third.Name);
        Assert.Equal("edited", await (await admin.GetAsync($"/api/admin/documents/files/{first.Id}/content")).Content.ReadAsStringAsync());

        // A web page never opens in the browser from the API's own address.
        var page = await (await UploadAsync(admin, worship.Id, null, "notes.html", "<script>alert(1)</script>", "text/html")).ReadAsync<DocumentDto>();
        var html = await admin.GetAsync($"/api/admin/documents/files/{page.Id}/content");
        Assert.Equal("attachment", html.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("application/octet-stream", html.Content.Headers.ContentType?.MediaType);

        // Someone who may only view sees the shared library, can't change it, and can't find the private one.
        var viewer = await SignInViewerAsync(admin);
        var visible = await (await viewer.GetAsync("/api/admin/documents/libraries")).ReadAsync<List<LibraryDto>>();
        Assert.Contains(visible, l => l.Id == worship.Id && l.Access == LibraryAccess.View);
        Assert.DoesNotContain(visible, l => l.Id == leadership.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(viewer, worship.Id, null, "mine.txt", "x")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/admin/documents/libraries/{leadership.Id}/folder")).StatusCode);
        var secret = await (await UploadAsync(admin, leadership.Id, null, "Budget.txt", "private")).ReadAsync<DocumentDto>();
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/admin/documents/files/{secret.Id}/content")).StatusCode);
        Assert.DoesNotContain(await (await viewer.GetAsync("/api/admin/documents/search?q=budget")).ReadAsync<List<DocumentHitDto>>(), h => h.Document.Id == secret.Id);

        // Deleting goes to the recycle bin, and the file can be put back.
        (await admin.DeleteAsync($"/api/admin/documents/files/{first.Id}")).EnsureSuccessStatusCode();
        var bin = await (await admin.GetAsync("/api/admin/documents/recycle-bin")).ReadAsync<List<BinItemDto>>();
        Assert.Contains(bin, i => i.Id == first.Id && i.Kind == "document");
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/admin/documents/files/{first.Id}/content")).StatusCode);
        (await admin.PostAsync($"/api/admin/documents/recycle-bin/document/{first.Id}/restore", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/admin/documents/files/{first.Id}/content")).StatusCode);
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid libraryId, Guid? folderId, string name, string text, string type = "text/plain")
    {
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
        file.Headers.ContentType = new MediaTypeHeaderValue(type);
        var form = new MultipartFormDataContent { { file, "file", name } };
        var folder = folderId is { } id ? $"?folderId={id}" : string.Empty;
        return client.PostAsync($"/api/admin/documents/libraries/{libraryId}/files{folder}", form);
    }

    private async Task<HttpClient> SignInViewerAsync(HttpClient admin)
    {
        var person = await (await admin.PostJsonAsync("/api/admin/people", new CreatePersonRequest("Docs", "Viewer", null, null, null, null, null, null, null))).ReadAsync<PersonDetailDto>();
        var setup = await (await admin.PostJsonAsync($"/api/admin/people/{person.Id}/staff-login", new { email = "docs.viewer@test.local" })).ReadAsync<StaffLoginSetupDto>();
        const string password = "a-long-enough-password";
        (await admin.PostJsonAsync("/api/auth/staff/set-password", new { setup.UserId, token = setup.SetupToken, password })).EnsureSuccessStatusCode();
        var role = await (await admin.PostJsonAsync("/api/admin/roles", new SaveRoleRequest("Documents viewer", null, ["documents.view"]))).ReadAsync<RoleDto>();
        await (await admin.PostJsonAsync("/api/admin/grants", new CreateGrantRequest(person.Id, role.Id, "shapers", null, null))).ReadAsync<GrantDto>();
        return await api.SignInStaffAsync("docs.viewer@test.local", password);
    }
}
