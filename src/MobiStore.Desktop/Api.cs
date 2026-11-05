using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace MobiStore.Desktop;
public sealed class Api
{
    public HttpClient Client { get; } = new() { Timeout = TimeSpan.FromSeconds(30) };
    public Api(string address) { Client.BaseAddress = new Uri(address.TrimEnd('/') + "/"); }
    public async Task<T> Get<T>(string path)
    {
        using var response = await Client.GetAsync(path); await Check(response);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return default!;
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    public async Task<T> Post<T>(string path, object payload)
    {
        using var response = await Client.PostAsJsonAsync(path, payload); await Check(response);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return default!;
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    public async Task Put(string path, object payload) { using var response = await Client.PutAsJsonAsync(path, payload); await Check(response); }
    public async Task Delete(string path) { using var response = await Client.DeleteAsync(path); await Check(response); }
    public async Task<T> PostLong<T>(string path)
    {
        using var client = new HttpClient { BaseAddress = Client.BaseAddress, Timeout = TimeSpan.FromMinutes(5) };
        foreach (var header in Client.DefaultRequestHeaders) client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
        using var response = await client.PostAsJsonAsync(path, new { }); await Check(response);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private static async Task Check(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var text = await response.Content.ReadAsStringAsync();
        try { var error = JsonDocument.Parse(text).RootElement.GetProperty("error").GetString(); throw new ApiException(error ?? text); }
        catch (JsonException) { throw new ApiException($"تعذر تنفيذ الطلب ({(int)response.StatusCode})"); }
        catch (KeyNotFoundException) { throw new ApiException($"تعذر تنفيذ الطلب ({(int)response.StatusCode})"); }
    }
}
public sealed class ApiException(string message) : Exception(message);
