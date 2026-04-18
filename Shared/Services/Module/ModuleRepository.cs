using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Shared.Services
{
    public static class ModuleRepository
    {
        private static readonly Serilog.ILogger Logger =
            Serilog.Log.ForContext("SourceContext", nameof(ModuleRepository));

        private const string RepositoryDirectory = "module";
        private static string RepositoryFile;
        private static string StateFile;

        private static readonly object SyncRoot = new();
        private static readonly HttpClient HttpClient;

        private static Dictionary<string, string> repositoryState;

        static ModuleRepository()
        {
            if (File.Exists(Path.Combine("mods", "repository.yaml")))
            {
                RepositoryFile = Path.Combine(Environment.CurrentDirectory, "mods", "repository.yaml");
                StateFile = Path.Combine(Environment.CurrentDirectory, "mods", ".repository_state.json");
            }
            else
            {
                RepositoryFile = Path.Combine(Environment.CurrentDirectory, RepositoryDirectory, "repository.yaml");
                StateFile = Path.Combine(Environment.CurrentDirectory, RepositoryDirectory, ".repository_state.json");
            }

            HttpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(60),
            };

            if (!HttpClient.DefaultRequestHeaders.UserAgent.Any())
                HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("LampacNGModuleRepository/1.0");

            if (!HttpClient.DefaultRequestHeaders.Accept.Any())
                HttpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        }

        public static void UpdateModules()
        {
            if (!Monitor.TryEnter(SyncRoot))
            {
                Log("UpdateModules skipped because another update is running");
                return;
            }

            Log("UpdateModules start");

            try
            {
                var repositories = LoadConfiguration();
                if (repositories.Count == 0)
                {
                    Log("No repositories configured");
                    return;
                }

                string repositoryDirectoryPath = Path.Combine(Environment.CurrentDirectory, RepositoryDirectory);
                Directory.CreateDirectory(repositoryDirectoryPath);
                Log($"Ensured repository directory exists at {repositoryDirectoryPath}");

                var state = LoadState();
                bool stateChanged = false;

                foreach (var repository in repositories)
                {
                    try
                    {
                        if (!repository.IsValid)
                        {
                            Log($"Skipping invalid repository '{repository?.Url}'");
                            continue;
                        }

                        bool missingModule = repository.Folders.Any(folder => !Directory.Exists(GetRepositoryModulePath(repository, folder)));
                        string commitSha = GetLatestCommitSha(repository, state, ref stateChanged);
                        if (string.IsNullOrEmpty(commitSha))
                        {
                            Log($"Could not determine latest commit for {repository.Url}");
                            continue;
                        }

                        string stateKey = repository.StateKey;
                        if (!missingModule && state.TryGetValue(stateKey, out string storedSha) && string.Equals(storedSha, commitSha, StringComparison.OrdinalIgnoreCase))
                        {
                            Log($"Repository '{repository.Url}' is up-to-date (sha={commitSha})");
                            continue;
                        }

                        if (DownloadAndExtract(repository))
                        {
                            state[stateKey] = commitSha;
                            stateChanged = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogError(ex, "Error processing repository {Message}",
                            $"{repository?.Url} - {ex.Message}");
                    }
                }

                if (stateChanged)
                {
                    SaveState(state);
                    Log("State saved");
                }
            }
            catch (Exception ex)
            {
                LogError(ex, "Unexpected error");
            }
            finally
            {
                Log("UpdateModules finished, releasing lock");
                Monitor.Exit(SyncRoot);
            }
        }

        private static List<RepositoryEntry> LoadConfiguration()
        {
            if (!File.Exists(RepositoryFile))
            {
                Log($"Repository config file not found at {RepositoryFile}");
                return new List<RepositoryEntry>();
            }

            try
            {
                string yaml = File.ReadAllText(RepositoryFile);
                if (string.IsNullOrWhiteSpace(yaml))
                {
                    Log("Repository config file is empty");
                    return new List<RepositoryEntry>();
                }

                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(UnderscoredNamingConvention.Instance)
                    .IgnoreUnmatchedProperties()
                    .Build();

                var document = deserializer.Deserialize(new StringReader(yaml));
                if (document == null)
                {
                    Log("Repository config deserialized to null");
                    return [];
                }

                var repos = ParseRepositories(document);
                Log($"Loaded {repos.Count} repository entries from config");
                return repos;
            }
            catch (Exception ex)
            {
                LogError(ex, "Failed to read configuration");
                return [];
            }
        }

        private static List<RepositoryEntry> ParseRepositories(object document)
        {
            var list = new List<RepositoryEntry>();

            if (document is IList<object> sequence)
            {
                foreach (var item in sequence)
                {
                    var repository = CreateRepository(item);
                    if (repository != null)
                        list.Add(repository);
                    else
                        Log("Skipped invalid repository entry in sequence");
                }
            }
            else if (document is IDictionary<object, object> map)
            {
                foreach (var entry in map)
                {
                    var repository = CreateRepository(entry.Value);
                    if (repository != null)
                        list.Add(repository);
                    else
                        Log("Skipped invalid repository entry in map");
                }
            }

            return list;
        }

        private static RepositoryEntry CreateRepository(object node)
        {
            if (node is IDictionary<object, object> map)
            {
                string url = GetString(map, "repository", "repo", "url", "git", "remote");
                if (string.IsNullOrWhiteSpace(url))
                {
                    Log("Repository entry missing url");
                    return null;
                }

                string branch = GetString(map, "branch", "ref");
                var folders = ParseFolders(map);

                var repository = new RepositoryEntry
                {
                    Url = url.Trim(),
                    Branch = string.IsNullOrWhiteSpace(branch) ? null : branch.Trim(),
                    Folders = folders
                };

                if (!TryParseGitHubUrl(repository.Url, out string owner, out string name))
                {
                    Log($"Unsupported repository url '{repository.Url}'");
                    return null;
                }

                repository.Owner = owner;
                repository.Name = name;

                ApplyAuthenticationSettings(map, repository);
                Log($"Parsed repository {repository.Owner}/{repository.Name} branch={repository.Branch ?? "(default)"}");

                // If no folders were specified in YAML, try to fetch top-level directories from GitHub repo
                if (repository.Folders == null || repository.Folders.Count == 0)
                {
                    try
                    {
                        var remoteFolders = FetchRepositoryFolders(repository);
                        if (remoteFolders.Count > 0)
                        {
                            repository.Folders = remoteFolders;
                            Log($"Populated {remoteFolders.Count} folders from remote repository {repository.Owner}/{repository.Name}");
                        }
                        else
                        {
                            Log($"No folders found in remote repository {repository.Owner}/{repository.Name}");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogError(ex, $"Failed to fetch folders for {repository.Owner}/{repository.Name} - {ex.Message}");
                    }
                }

                return repository;
            }

            return null;
        }

        private static void ApplyAuthenticationSettings(IDictionary<object, object> map, RepositoryEntry repository)
        {
            if (map == null || repository == null)
                return;

            string accept = GetString(map, "accept", "accept_header");
            if (!string.IsNullOrWhiteSpace(accept))
                repository.AcceptHeader = accept.Trim();

            string authHeader = GetString(map, "auth_header", "authorization", "authorization_header");
            if (!string.IsNullOrWhiteSpace(authHeader))
            {
                string resolvedHeader = ResolveSecretValue(authHeader, "auth_header", repository);
                if (!string.IsNullOrWhiteSpace(resolvedHeader))
                    repository.Token = resolvedHeader.Trim();

                return;
            }

            string tokenValue = GetString(map, "token", "pat", "personal_access_token");
            if (string.IsNullOrWhiteSpace(tokenValue))
                return;

            string resolvedToken = ResolveSecretValue(tokenValue, "token", repository);
            if (string.IsNullOrWhiteSpace(resolvedToken))
                return;

            string tokenType = GetString(map, "token_type", "auth_type", "authorization_scheme", "scheme", "token_scheme");
            string headerValue;

            if (!string.IsNullOrWhiteSpace(tokenType))
            {
                headerValue = $"{tokenType.Trim()} {resolvedToken.Trim()}".Trim();
            }
            else
            {
                string trimmed = resolvedToken.Trim();
                headerValue = trimmed.Contains(' ') ? trimmed : $"token {trimmed}";
            }

            if (string.IsNullOrWhiteSpace(headerValue))
            {
                Log($"Resolved token for {repository.Url} is empty");
                return;
            }

            repository.Token = headerValue;
        }

        private static string ResolveSecretValue(string value, string fieldName, RepositoryEntry repository)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            string trimmed = value.Trim();

            int envIndex = trimmed.IndexOf("env:", StringComparison.OrdinalIgnoreCase);
            if (envIndex < 0)
                return trimmed;

            var builder = new StringBuilder();
            int currentIndex = 0;

            while (envIndex >= 0)
            {
                builder.Append(trimmed, currentIndex, envIndex - currentIndex);

                int nameStart = envIndex + 4;
                int nameEnd = nameStart;
                while (nameEnd < trimmed.Length && (char.IsLetterOrDigit(trimmed[nameEnd]) || trimmed[nameEnd] == '_'))
                    nameEnd++;

                if (nameEnd == nameStart)
                {
                    Log($"{fieldName} environment variable name is missing for repository {repository?.Url}");
                    return null;
                }

                string envName = trimmed[nameStart..nameEnd];
                string envValue = Environment.GetEnvironmentVariable(envName);
                if (string.IsNullOrWhiteSpace(envValue))
                {
                    Log($"Environment variable '{envName}' not found for repository {repository?.Url}");
                    return null;
                }

                builder.Append(envValue.Trim());
                currentIndex = nameEnd;
                envIndex = trimmed.IndexOf("env:", currentIndex, StringComparison.OrdinalIgnoreCase);
            }

            builder.Append(trimmed[currentIndex..]);
            return builder.ToString().Trim();
        }

        private static List<RepositoryFolder> ParseFolders(IDictionary<object, object> map)
        {
            foreach (string key in new[] { "modules", "folders", "directories", "paths", "include" })
            {
                if (TryGetValue(map, key, out object value))
                    return ConvertToFolders(value);
            }

            return new List<RepositoryFolder>();
        }

        private static List<RepositoryFolder> ConvertToFolders(object value)
        {
            var result = new List<RepositoryFolder>();

            if (value is IList<object> sequence)
            {
                foreach (var item in sequence)
                {
                    var folder = ConvertFolderItem(item);
                    if (folder != null)
                        result.Add(folder);
                    else
                        Log("Skipped invalid folder item in sequence");
                }
            }
            else if (value is IDictionary<object, object> map)
            {
                foreach (var entry in map)
                {
                    var folder = ConvertFolderEntry(entry.Key, entry.Value);
                    if (folder != null)
                        result.Add(folder);
                    else
                        Log("Skipped invalid folder entry in map");
                }
            }

            return result;
        }

        private static RepositoryFolder ConvertFolderItem(object item)
        {
            if (item is string str)
                return CreateFolder(str, null);

            if (item is IDictionary<object, object> map)
            {
                string source = GetString(map, "path", "source", "folder", "repo_path", "from");
                string target = GetString(map, "target", "name", "to", "destination");

                if (string.IsNullOrEmpty(source) && map.Count == 1)
                {
                    var single = map.First();
                    source = single.Key?.ToString();
                    target = single.Value?.ToString();
                }

                return CreateFolder(source, target);
            }

            return null;
        }

        private static RepositoryFolder ConvertFolderEntry(object key, object value)
        {
            if (value is IDictionary<object, object> map)
            {
                string source = GetString(map, "path", "source", "folder", "repo_path", "from") ?? key?.ToString();
                string target = GetString(map, "target", "name", "to", "destination") ?? value?.ToString();
                return CreateFolder(source, target);
            }

            return CreateFolder(key?.ToString(), value?.ToString());
        }

        private static RepositoryFolder CreateFolder(string source, string target)
        {
            if (string.IsNullOrWhiteSpace(source))
                return null;

            var folder = new RepositoryFolder(source, target);
            if (!folder.IsValid)
                return null;

            return folder;
        }

        private static string GetString(IDictionary<object, object> map, params string[] keys)
        {
            foreach (var key in keys)
            {
                foreach (var entry in map)
                {
                    if (string.Equals(entry.Key?.ToString(), key, StringComparison.OrdinalIgnoreCase))
                        return entry.Value?.ToString();
                }
            }

            return null;
        }

        private static bool TryGetValue(IDictionary<object, object> map, string key, out object value)
        {
            foreach (var entry in map)
            {
                if (string.Equals(entry.Key?.ToString(), key, StringComparison.OrdinalIgnoreCase))
                {
                    value = entry.Value;
                    return true;
                }
            }

            value = null;
            return false;
        }

        private static Dictionary<string, string> LoadState()
        {
            if (repositoryState != null)
                return repositoryState;

            if (File.Exists(StateFile))
            {
                try
                {
                    var json = File.ReadAllText(StateFile);
                    var data = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                    if (data != null)
                        repositoryState = new Dictionary<string, string>(data, StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception ex)
                {
                    LogError(ex, "Failed to load state");
                }
            }

            repositoryState ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Log($"Loaded state entries = {repositoryState.Count}");
            return repositoryState;
        }

        private static void SaveState(Dictionary<string, string> state)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StateFile));
                File.WriteAllText(StateFile, JsonConvert.SerializeObject(state, Formatting.Indented));
            }
            catch (Exception ex)
            {
                LogError(ex, "Failed to save state");
            }
        }

        private static string GetLatestCommitSha(RepositoryEntry repository, Dictionary<string, string> state, ref bool stateChanged)
        {
            if (string.IsNullOrEmpty(repository.Owner) || string.IsNullOrEmpty(repository.Name))
            {
                Log("Get latest commit SHA - owner or name is empty");
                return null;
            }

            if (!TryResolveBranchAndCommit(repository, state, ref stateChanged, out string branch, out string sha))
            {
                Log($"Could not determine a valid branch for {repository.Owner}/{repository.Name}");
                return null;
            }

            repository.Branch = branch;
            Log($"Latest commit sha for {repository.Owner}/{repository.Name} ({branch}) = {sha}");
            return sha;
        }

        private static bool TryResolveBranchAndCommit(RepositoryEntry repository, Dictionary<string, string> state, ref bool stateChanged, out string branch, out string sha)
        {
            branch = null;
            sha = null;

            if (string.IsNullOrEmpty(repository.Owner) || string.IsNullOrEmpty(repository.Name))
                return false;

            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(repository.Branch))
                candidates.Add(repository.Branch.Trim());

            string defaultBranch = GetRepositoryDefaultBranch(repository, state, ref stateChanged);
            if (!string.IsNullOrWhiteSpace(defaultBranch) && !candidates.Contains(defaultBranch, StringComparer.OrdinalIgnoreCase))
                candidates.Add(defaultBranch);

            if (!candidates.Contains("main", StringComparer.OrdinalIgnoreCase))
                candidates.Add("main");
            if (!candidates.Contains("master", StringComparer.OrdinalIgnoreCase))
                candidates.Add("master");

            foreach (string candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                string candidateSha = GetBranchCommitSha(repository, candidate, state, ref stateChanged);
                if (string.IsNullOrWhiteSpace(candidateSha))
                    continue;

                branch = candidate;
                sha = candidateSha;
                Log($"Selected branch '{candidate}' for {repository.Owner}/{repository.Name}");
                return true;
            }

            return false;
        }

        private static string DetermineBranch(RepositoryEntry repository)
        {
            if (string.IsNullOrEmpty(repository.Owner) || string.IsNullOrEmpty(repository.Name))
                return null;

            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(repository.Branch))
                candidates.Add(repository.Branch.Trim());

            // Try to get default branch from repo metadata
            var repoInfo = GetJson(repository, $"https://api.github.com/repos/{repository.Owner}/{repository.Name}");
            var defaultBranch = repoInfo?["default_branch"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(defaultBranch) && !candidates.Contains(defaultBranch, StringComparer.OrdinalIgnoreCase))
                candidates.Add(defaultBranch);

            // Add common fallbacks
            if (!candidates.Contains("main", StringComparer.OrdinalIgnoreCase))
                candidates.Add("main");
            if (!candidates.Contains("master", StringComparer.OrdinalIgnoreCase))
                candidates.Add("master");

            foreach (var b in candidates)
            {
                if (string.IsNullOrWhiteSpace(b))
                    continue;

                var branchInfo = GetJson(repository, $"https://api.github.com/repos/{repository.Owner}/{repository.Name}/branches/{Uri.EscapeDataString(b)}");
                if (branchInfo != null)
                {
                    repository.Branch = b;
                    Log($"Selected branch '{b}' for {repository.Owner}/{repository.Name}");
                    return b;
                }
            }

            return null;
        }

        private static string GetRepositoryDefaultBranch(RepositoryEntry repository, Dictionary<string, string> state, ref bool stateChanged)
        {
            string url = $"https://api.github.com/repos/{repository.Owner}/{repository.Name}";
            string etagKey = GetRepositoryInfoEtagKey(repository);
            string branchKey = GetRepositoryDefaultBranchStateKey(repository);

            var result = GetJsonConditional(repository, url, GetStateValue(state, etagKey));
            if (result == null)
                return GetStateValue(state, branchKey);

            if (result.NotModified)
            {
                string cachedBranch = GetStateValue(state, branchKey);
                if (!string.IsNullOrWhiteSpace(cachedBranch))
                {
                    Log($"Repository metadata for {repository.Owner}/{repository.Name} not modified, using cached default branch '{cachedBranch}'");
                    return cachedBranch;
                }

                result = GetJsonConditional(repository, url, null);
                if (result == null || result.NotModified)
                    return null;
            }

            string defaultBranch = result.Json?["default_branch"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(defaultBranch))
                SetStateValue(state, branchKey, defaultBranch, ref stateChanged);

            SetStateValue(state, etagKey, result.ETag, ref stateChanged);
            return defaultBranch;
        }

        private static string GetBranchCommitSha(RepositoryEntry repository, string branch, Dictionary<string, string> state, ref bool stateChanged)
        {
            string url = $"https://api.github.com/repos/{repository.Owner}/{repository.Name}/branches/{Uri.EscapeDataString(branch)}";
            string etagKey = GetBranchInfoEtagKey(repository, branch);
            string shaKey = GetBranchCommitShaStateKey(repository, branch);

            var result = GetJsonConditional(repository, url, GetStateValue(state, etagKey));
            if (result == null)
                return GetStateValue(state, shaKey);

            if (result.NotModified)
            {
                string cachedSha = GetStateValue(state, shaKey);
                if (!string.IsNullOrWhiteSpace(cachedSha))
                {
                    Log($"Branch metadata for {repository.Owner}/{repository.Name} ({branch}) not modified, using cached sha {cachedSha}");
                    return cachedSha;
                }

                result = GetJsonConditional(repository, url, null);
                if (result == null || result.NotModified)
                    return null;
            }

            string sha = result.Json?["commit"]?["sha"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(sha))
                SetStateValue(state, shaKey, sha, ref stateChanged);

            SetStateValue(state, etagKey, result.ETag, ref stateChanged);
            return sha;
        }

        private static HttpResponseMessage SendGetRequest(string url, RepositoryEntry repository, string acceptOverride = null, bool includeConfiguredAccept = true, string ifNoneMatch = null)
        {
            var request = CreateRequest(HttpMethod.Get, url, repository, acceptOverride, includeConfiguredAccept, ifNoneMatch);

            try
            {
                return HttpClient.SendAsync(request).GetAwaiter().GetResult();
            }
            finally
            {
                request.Dispose();
            }
        }

        private static HttpRequestMessage CreateRequest(HttpMethod method, string url, RepositoryEntry repository, string acceptOverride, bool includeConfiguredAccept, string ifNoneMatch = null)
        {
            var request = new HttpRequestMessage(method, url);

            if (!string.IsNullOrWhiteSpace(repository?.Token))
                request.Headers.TryAddWithoutValidation("Authorization", repository.Token);

            if (includeConfiguredAccept && !string.IsNullOrWhiteSpace(repository?.AcceptHeader))
                request.Headers.TryAddWithoutValidation("Accept", repository.AcceptHeader);

            if (!string.IsNullOrWhiteSpace(acceptOverride))
                request.Headers.TryAddWithoutValidation("Accept", acceptOverride);

            if (!string.IsNullOrWhiteSpace(ifNoneMatch))
                request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);

            return request;
        }

        private static JsonRequestResult GetJsonConditional(RepositoryEntry repository, string url, string ifNoneMatch)
        {
            try
            {
                using var response = SendGetRequest(url, repository, ifNoneMatch: ifNoneMatch);
                string etag = GetHeaderValue(response, "ETag");

                if (response.StatusCode == HttpStatusCode.NotModified)
                    return new JsonRequestResult { NotModified = true, ETag = string.IsNullOrWhiteSpace(etag) ? ifNoneMatch : etag };

                if (!response.IsSuccessStatusCode)
                {
                    Log($"Request {url} failed with {(int)response.StatusCode} {response.StatusCode}");
                    return null;
                }

                string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (string.IsNullOrEmpty(json))
                    return new JsonRequestResult { ETag = etag };

                Log($"Get JsonConditional success for {url}");
                return new JsonRequestResult
                {
                    ETag = etag,
                    Json = JsonConvert.DeserializeObject<JObject>(json)
                };
            }
            catch (Exception ex)
            {
                LogError(ex, $"Request {url} failed");
                return null;
            }
        }

        private static JObject GetJson(RepositoryEntry repository, string url)
        {
            try
            {
                using var response = SendGetRequest(url, repository);
                if (!response.IsSuccessStatusCode)
                {
                    Log($"Request {url} failed with {(int)response.StatusCode} {response.StatusCode}");
                    return null;
                }

                string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (string.IsNullOrEmpty(json))
                    return null;

                Log($"Get Json success for {url}");
                return JsonConvert.DeserializeObject<JObject>(json);
            }
            catch (Exception ex)
            {
                LogError(ex, $"Request {url} failed");
                return null;
            }
        }

        private static JArray GetJsonArray(RepositoryEntry repository, string url)
        {
            try
            {
                using var response = SendGetRequest(url, repository);
                if (!response.IsSuccessStatusCode)
                {
                    Log($"Request {url} failed with {(int)response.StatusCode} {response.StatusCode}");
                    return null;
                }

                string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (string.IsNullOrEmpty(json))
                    return null;

                Log($"Get JsonArray success for {url}");
                return JsonConvert.DeserializeObject<JArray>(json);
            }
            catch (Exception ex)
            {
                LogError(ex, $"Request {url} failed");
                return null;
            }
        }

        private static string GetHeaderValue(HttpResponseMessage response, string headerName)
        {
            if (response == null || string.IsNullOrWhiteSpace(headerName))
                return null;

            if (response.Headers.TryGetValues(headerName, out var values))
                return values.FirstOrDefault();

            if (response.Content?.Headers != null && response.Content.Headers.TryGetValues(headerName, out values))
                return values.FirstOrDefault();

            return null;
        }

        private static string GetStateValue(Dictionary<string, string> state, string key)
        {
            if (state == null || string.IsNullOrWhiteSpace(key))
                return null;

            return state.TryGetValue(key, out string value) ? value : null;
        }

        private static void SetStateValue(Dictionary<string, string> state, string key, string value, ref bool stateChanged)
        {
            if (state == null || string.IsNullOrWhiteSpace(key))
                return;

            if (string.IsNullOrWhiteSpace(value))
            {
                if (state.Remove(key))
                    stateChanged = true;

                return;
            }

            if (state.TryGetValue(key, out string existing) && string.Equals(existing, value, StringComparison.Ordinal))
                return;

            state[key] = value;
            stateChanged = true;
        }

        private static string GetRepositoryIdentity(RepositoryEntry repository) =>
            $"{repository.Owner}/{repository.Name}".ToLowerInvariant();

        private static string GetRepositoryInfoEtagKey(RepositoryEntry repository) =>
            $"etag:repo:{GetRepositoryIdentity(repository)}";

        private static string GetRepositoryDefaultBranchStateKey(RepositoryEntry repository) =>
            $"cache:repo-default-branch:{GetRepositoryIdentity(repository)}";

        private static string GetBranchInfoEtagKey(RepositoryEntry repository, string branch) =>
            $"etag:branch:{GetRepositoryIdentity(repository)}:{branch}";

        private static string GetBranchCommitShaStateKey(RepositoryEntry repository, string branch) =>
            $"cache:branch-sha:{GetRepositoryIdentity(repository)}:{branch}";

        private static List<RepositoryFolder> FetchRepositoryFolders(RepositoryEntry repository)
        {
            var result = new List<RepositoryFolder>();
            if (string.IsNullOrEmpty(repository.Owner) || string.IsNullOrEmpty(repository.Name))
                return result;

            var branch = DetermineBranch(repository);
            if (string.IsNullOrEmpty(branch))
                return result;

            string url = $"https://api.github.com/repos/{repository.Owner}/{repository.Name}/contents?ref={Uri.EscapeDataString(branch)}";
            var items = GetJsonArray(repository, url);
            if (items == null)
                return result;

            foreach (var item in items)
            {
                var type = item["type"]?.Value<string>();
                if (!string.Equals(type, "dir", StringComparison.OrdinalIgnoreCase))
                    continue;

                var name = item["name"]?.Value<string>();
                if (string.IsNullOrEmpty(name))
                    continue;

                var folder = new RepositoryFolder(name, null);
                if (folder.IsValid)
                    result.Add(folder);
            }

            return result;
        }

        private static bool DownloadAndExtract(RepositoryEntry repository)
        {
            string branch = string.IsNullOrWhiteSpace(repository.Branch) ? "main" : repository.Branch;
            string archiveUrl = $"https://codeload.github.com/{repository.Owner}/{repository.Name}/zip/refs/heads/{Uri.EscapeDataString(branch)}";
            string tempZip = Path.Combine(Path.GetTempPath(), $"lampac-modrepo-{Guid.NewGuid():N}.zip");
            string tempDir = Path.Combine(Path.GetTempPath(), $"lampac-modrepo-{Guid.NewGuid():N}");

            Log($"Download and extract start for {repository.Owner}/{repository.Name} branch={branch}");

            try
            {
                Log($"Downloading archive {archiveUrl}");
                using (var response = SendGetRequest(archiveUrl, repository))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        Log($"Failed to download {archiveUrl} - {(int)response.StatusCode}{response.StatusCode}");
                        return false;
                    }

                    using (var stream = File.Create(tempZip))
                        response.Content.CopyToAsync(stream).GetAwaiter().GetResult();
                }

                ZipFile.ExtractToDirectory(tempZip, tempDir, true);
                Log($"Archive extracted to {tempDir}");

                string root = Directory.GetDirectories(tempDir).FirstOrDefault();
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                {
                    Log("Archive structure not recognized");
                    return false;
                }

                Log($"Archive root = {root}");

                foreach (var folder in repository.Folders)
                {
                    string sourcePath = folder.GetSourcePath(root);
                    if (!Directory.Exists(sourcePath))
                    {
                        Log($"Folder '{folder.Source}' not found in {repository.Url}");
                        continue;
                    }

                    string destinationPath = GetRepositoryModulePath(repository, folder);

                    string existingManifestJson = null;
                    string existingManifestPath = Path.Combine(destinationPath, "manifest.json");

                    if (Directory.Exists(destinationPath))
                    {
                        try
                        {
                            // Read existing manifest if present so we can preserve/merge its values
                            if (File.Exists(existingManifestPath))
                            {
                                try { existingManifestJson = File.ReadAllText(existingManifestPath); } catch { existingManifestJson = null; }
                            }

                            Directory.Delete(destinationPath, true);
                        }
                        catch (Exception ex)
                        {
                            LogError(ex, $"Failed to clean '{destinationPath}'");
                            continue;
                        }
                    }

                    Directory.CreateDirectory(destinationPath);
                    CopyDirectory(sourcePath, destinationPath);

                    // After copying, merge manifests if we had an existing one
                    string newManifestPath = Path.Combine(destinationPath, "manifest.json");
                    if (!string.IsNullOrEmpty(existingManifestJson) && File.Exists(newManifestPath))
                    {
                        try
                        {
                            string newManifestJson = File.ReadAllText(newManifestPath);
                            string merged = MergeManifests(existingManifestJson, newManifestJson);
                            File.WriteAllText(newManifestPath, merged);
                        }
                        catch (Exception ex)
                        {
                            LogError(ex, $"Failed to merge manifest.json for '{folder.ModuleName}'");
                        }
                    }

                    Log($"Updated module '{folder.ModuleName}' from {repository.Url}");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogError(ex, "Unexpected error");
                return false;
            }
            finally
            {
                try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
                Log($"Download and extract finished for {repository.Owner}/{repository.Name}");
            }
        }

        private static string GetRepositoryModulePath(RepositoryEntry repository, RepositoryFolder folder)
        {
            return Path.Combine(
               Environment.CurrentDirectory,
               RepositoryDirectory,
               repository.Name,
               folder.ModuleName
           );
        }

        private static string MergeManifests(string existingJson, string newJson)
        {
            try
            {
                var existingObj = JsonConvert.DeserializeObject<JObject>(existingJson);
                var newObj = JsonConvert.DeserializeObject<JObject>(newJson);

                if (existingObj == null)
                    return newJson;

                if (newObj == null)
                    return existingJson;

                // Основа - новый manifest
                var result = (JObject)newObj.DeepClone();

                foreach (var oldProp in existingObj.Properties())
                {
                    var oldName = oldProp.Name;

                    // enable и dynamic всегда сохраняем из старого, если они там есть
                    if (string.Equals(oldName, "enable", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(oldName, "dynamic", StringComparison.OrdinalIgnoreCase))
                    {
                        result[oldName] = oldProp.Value.DeepClone();
                        continue;
                    }

                    // Любые кастомные поля переносим только если их нет в новом
                    var existsInNew = result.Properties()
                        .Any(p => string.Equals(p.Name, oldName, StringComparison.OrdinalIgnoreCase));

                    if (!existsInNew)
                        result[oldName] = oldProp.Value.DeepClone();
                }

                return JsonConvert.SerializeObject(result, Formatting.Indented);
            }
            catch
            {
                return newJson;
            }
        }

        private static void CopyDirectory(string source, string destination)
        {
            foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(source, directory);
                if (ShouldSkip(relative))
                    continue;

                Directory.CreateDirectory(Path.Combine(destination, relative));
            }

            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(source, file);
                if (ShouldSkip(relative))
                    continue;

                string target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target));

                try
                {
                    File.Copy(file, target, true);
                }
                catch (Exception ex)
                {
                    LogError(ex, $"Failed to copy file '{file}' to '{target}'");
                }
            }
        }

        private static bool ShouldSkip(string relative)
        {
            if (string.IsNullOrEmpty(relative))
                return false;

            string normalized = relative.Replace('\\', '/');
            if (normalized.StartsWith(".git", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith(".github", StringComparison.OrdinalIgnoreCase))
                return true;

            string fileName = Path.GetFileName(normalized);
            if (string.Equals(fileName, ".gitignore", StringComparison.OrdinalIgnoreCase) || string.Equals(fileName, ".gitattributes", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private static bool TryParseGitHubUrl(string url, out string owner, out string name)
        {
            owner = null;
            name = null;

            if (string.IsNullOrWhiteSpace(url))
                return false;

            string working = url.Trim();

            if (working.StartsWith("git@", StringComparison.OrdinalIgnoreCase))
            {
                int index = working.IndexOf(':');
                if (index != -1 && working.Length > index + 1)
                    working = working[(index + 1)..];
            }

            if (!working.StartsWith("http", StringComparison.OrdinalIgnoreCase) && working.Contains("github.com"))
                working = "https://" + working.TrimStart('/');

            if (Uri.TryCreate(working, UriKind.Absolute, out var uri) && uri.Host.EndsWith("github.com", StringComparison.OrdinalIgnoreCase))
            {
                var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length >= 2)
                {
                    owner = segments[0];
                    name = segments[1];
                }
            }
            else
            {
                var parts = working.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    owner = parts[^2];
                    name = parts[^1];
                }
            }

            if (!string.IsNullOrEmpty(name) && name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                name = name[..^4];

            return !string.IsNullOrEmpty(owner) && !string.IsNullOrEmpty(name);
        }

        private static void Log(string message)
        {
            Console.WriteLine($"ModuleRepository: {message}");
        }

        private static void LogError(Exception ex, string message, params object[] args)
        {
            Log(message);
            Logger.Error(ex, message, args);
        }

        private sealed class RepositoryEntry
        {
            public string Url { get; set; }
            public string Branch { get; set; }
            public string Owner { get; set; }
            public string Name { get; set; }
            public string Token { get; set; }
            public string AcceptHeader { get; set; }
            public List<RepositoryFolder> Folders { get; set; } = new List<RepositoryFolder>();

            public bool IsValid => !string.IsNullOrEmpty(Url) && !string.IsNullOrEmpty(Owner) && !string.IsNullOrEmpty(Name) && Folders.Count > 0;

            public string StateKey => $"repo:{Url}|{Branch}";
        }

        private sealed class JsonRequestResult
        {
            public JObject Json { get; set; }

            public string ETag { get; set; }

            public bool NotModified { get; set; }
        }

        private sealed class RepositoryFolder
        {
            public RepositoryFolder(string source, string target)
            {
                Source = Normalize(source);
                ModuleName = NormalizeTarget(target, Source);
            }

            public string Source { get; }

            public string ModuleName { get; }

            public bool IsValid => !string.IsNullOrEmpty(Source) && !string.IsNullOrEmpty(ModuleName);

            private static string Normalize(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return null;

                string trimmed = value.Trim().Replace('\\', '/').Trim('/');
                if (trimmed.Contains(".."))
                    return null;

                return trimmed;
            }

            private static string NormalizeTarget(string target, string source)
            {
                string normalized = Normalize(target);
                if (string.IsNullOrEmpty(normalized))
                    normalized = Normalize(source);

                if (string.IsNullOrEmpty(normalized))
                    return null;

                var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length == 0)
                    return null;

                return segments[^1].Replace('/', Path.DirectorySeparatorChar);
            }

            public string GetSourcePath(string root)
            {
                string path = root;
                foreach (string part in Source.Split('/', StringSplitOptions.RemoveEmptyEntries))
                    path = Path.Combine(path, part);

                return path;
            }
        }
    }
}