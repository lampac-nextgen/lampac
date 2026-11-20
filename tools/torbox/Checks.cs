using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Models.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IOFile = System.IO.File;

namespace TorBox;

// Compiled beside the unchanged dynamic manifest sources, never loaded by Lampac.
public partial class TorBoxController
{
    const string testUid="offline-account", testKey="offline-placeholder-key";
    static int checks;
    static void Check(string name,bool success) {
        if(!success)throw new Exception("FAIL: "+name);
        checks++;Console.WriteLine("PASS: "+name);
    }
    static (int Status,JObject Json) Result(ActionResult result) {
        var content=(ContentResult)result;
        return (content.StatusCode??200,JObject.Parse(content.Content));
    }
    static TorBoxController Controller(string uid=testUid,bool anonymous=false,string key=null,string mode=null,JObject config=null) {
        var context=new DefaultHttpContext();
        context.Request.Scheme="https";context.Request.Host=new HostString("lampac.example");
        context.Features.Set(new RequestModel{user_uid=uid,IsAnonymousRequest=anonymous});
        if(key!=null)context.Request.Headers["X-TorBox-Key"]=key;
        if(mode!=null)context.Request.Headers["X-TorBox-Mode"]=mode;
        if(config!=null)context.Request.Headers["X-TorBox-Config"]=Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(config.ToString()));
        return new TorBoxController{ControllerContext=new ControllerContext{HttpContext=context}};
    }
    static void Policy(bool clientMode=false,bool external=false) => IOFile.WriteAllText(configPath,new JObject{
        ["allow_client_credentials"]=clientMode,["allow_external_playlists"]=external,
        ["api_key"]=testKey,["allowed_uids"]=new JArray(testUid),
        ["torrent_origins"]=new JArray("https://server-only.example"),
        ["jacred"]=new JObject{["base_url"]="https://parser.example",["api_key"]="offline-parser-key"}
    }.ToString());
    static async Task<(int Status,JObject Json)> Probe(TorBoxController c) => Result(await c.Guard((config,key)=>
        Task.FromResult(c.Payload(new{key,config,owner=c.metadataOwner,external=c.externalPlaylistsAllowed}))));

    sealed class MockHttp : HttpMessageHandler {
        public int Calls,Mutations;
        public int Delay;
        public string Mode="normal";
        public JObject Mutation;
        public List<string> Hashes=new();
        public List<bool> HasCancellation=new();
        public void Reset(string mode="normal") {
            Calls=Mutations=Delay=0;Mode=mode;Mutation=null;Hashes.Clear();HasCancellation.Clear();
            lock(badgeBatchGate){badgeBatches.Clear();badgeBackoff.Clear();}
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation) {
            // Replace the handler, not production URLs. No socket can be opened by this mock.
            if(request.RequestUri.Host!="api.torbox.app" && request.RequestUri.Host!="parser.example")
                throw new Exception("Unexpected mock destination");
            Interlocked.Increment(ref Calls);
            lock(HasCancellation)HasCancellation.Add(cancellation.CanBeCanceled);
            if(Delay>0)await Task.Delay(Delay,cancellation);
            if(Mode=="exception")throw new HttpRequestException("https://parser.example/?apikey=offline-parser-key");
            if(Mode=="429")return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            if(request.RequestUri.Host=="parser.example")return Reply(HttpStatusCode.OK,new JObject{["Results"]=new JArray()});
            if(request.RequestUri.AbsolutePath.EndsWith("controltorrent")) {
                Interlocked.Increment(ref Mutations);
                Mutation=JObject.Parse(await request.Content.ReadAsStringAsync());
                return Reply(HttpStatusCode.OK,new JObject{["success"]=true});
            }
            if(request.RequestUri.AbsolutePath.EndsWith("checkcached")) {
                var query=Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.RequestUri.Query);
                var hashes=query["hash"].ToString().Split(',');
                lock(Hashes)Hashes.AddRange(hashes);
                var data=new JObject();
                for(int i=0;i<hashes.Length;i++)data[hashes[i]]=i%3==0?new JObject{["cached"]=true}:i%3==1?new JObject():new JObject{["cached"]="invalid"};
                return Reply(HttpStatusCode.OK,new JObject{["success"]=true,["data"]=Mode=="malformed"?new JArray():data});
            }
            if(Mode=="missing")return Reply(HttpStatusCode.NotFound,new JObject{["success"]=true,["error"]="ITEM_NOT_FOUND"});
            if(Mode=="wrong404")return Reply(HttpStatusCode.NotFound,new JObject{["success"]=true,["error"]="OTHER_ERROR"});
            if(Mode=="badlist")return Reply(HttpStatusCode.OK,new JObject{["success"]=true,["data"]="invalid"});
            return Reply(HttpStatusCode.OK,new JObject{["success"]=true,["data"]=new JArray(new JObject{["id"]=7,["name"]="Offline movie"})});
        }
        static HttpResponseMessage Reply(HttpStatusCode status,JObject data) => new(status){Content=new StringContent(data.ToString())};
    }

    public static async Task Main() {
        CoreInit.conf=new CoreInit();
        var mock=new MockHttp();
        // Test-only interception of the existing HttpClient instances. Fail before any
        // test if the runtime implementation changes; never fall back to real networking.
        var handler=typeof(HttpMessageInvoker).GetField("_handler",BindingFlags.Instance|BindingFlags.NonPublic)
            ?? throw new Exception("Cannot install offline HTTP handler");
        handler.SetValue(client,mock);handler.SetValue(publicClient,mock);
        await CredentialChecks(mock);
        await CacheChecks(mock);
        await DeletionChecks(mock);
        await ExternalChecks(mock);
        LogChecks();
        Console.WriteLine($"TorBox: {checks} checks passed (offline, mock mutations only).");
    }

    static async Task CredentialChecks(MockHttp mock) {
        Policy();mock.Reset();
        var probe=await Probe(Controller(key:"ignored-personal-key",config:new JObject{["invalid"]=true}));
        Check("file credentials ignore personal headers",probe.Status==200 && (string)probe.Json["key"]==testKey);
        Check("file mode enforces UID",(await Probe(Controller(uid:"other"))).Status==403);
        Check("anonymous denied",(await Probe(Controller(anonymous:true))).Status==403);
        Check("empty UID denied",(await Probe(Controller(uid:""))).Status==403);
        Check("header cannot enable client mode",(await Probe(Controller(mode:"client",key:testKey))).Status==403);
        var multiple=Controller();multiple.Request.Headers.Append("X-TorBox-Mode",new[]{"file","client"});
        Check("multiple mode headers denied",(await Probe(multiple)).Status==400);
        Policy(true);
        Check("client mode has no file key fallback",(await Probe(Controller())).Status==503);
        Check("invalid personal key denied",(await Probe(Controller(key:"short"))).Status==503);
        probe=await Probe(Controller(uid:"personal-user",key:testKey));
        Check("client mode ignores server UID list",probe.Status==200);
        Check("client mode ignores parser and origins from file",probe.Json["config"]?["jacred"]==null && !probe.Json["config"]["torrent_origins"].HasValues);
        Check("client header cannot select file mode",(await Probe(Controller(mode:"file",key:testKey))).Status==403);
        Check("client UID restriction applied",(await Probe(Controller(key:testKey,config:new JObject{["allowed_uids"]=new JArray("other")}))).Status==403);
        Check("client empty UID list means current account",(await Probe(Controller(key:testKey,config:new JObject{["allowed_uids"]=new JArray()}))).Status==200);
        Check("client cannot enable external playlists",(await Probe(Controller(key:testKey,config:new JObject{["allow_external_playlists"]=true}))).Status==400);
        Check("private parser URL denied",(await Probe(Controller(key:testKey,config:new JObject{["jacred"]=new JObject{["base_url"]="http://127.0.0.1",["api_key"]="dummy"}}))).Status==400);
        IOFile.WriteAllText(configPath,"{\"allow_client_credentials\":\"true\"}");
        Check("nonboolean policy fails closed",(await Probe(Controller(key:testKey))).Status==503);
        IOFile.WriteAllText(configPath,"{");
        Check("malformed policy fails closed",(await Probe(Controller(key:testKey))).Status>=400);
        IOFile.Delete(configPath);
        Check("missing files fail closed",(await Probe(Controller(key:testKey))).Status>=400);
        IOFile.WriteAllText(settingsPath,"{\"allow_client_credentials\":true}");
        Check("explicit settings-only client policy works",(await Probe(Controller(key:testKey))).Status==200);
        IOFile.WriteAllText(configPath,"{");
        Check("malformed private file cannot fall back to settings",(await Probe(Controller(key:testKey))).Status>=400);
        IOFile.Delete(settingsPath);Policy();
        Check("credential checks never use network",mock.Calls==0);
        mock.Reset("exception");
        var parserFailure=Result(await Controller().Online("Offline title","Offline title"));
        Check("parser exception cannot expose URL or key",parserFailure.Status==502 && !parserFailure.Json.ToString().Contains("offline-parser-key") && !parserFailure.Json.ToString().Contains("parser.example"));
        mock.Reset();
    }

    static async Task CacheChecks(MockHttp mock) {
        Policy();mock.Reset();
        string[] hashes=Enumerable.Range(0,100).Select(i=>i.ToString("x40")).ToArray();
        foreach(var invalid in new[]{Array.Empty<string>(),new[]{"invalid"},new string[101]})
            Check("invalid cache batch denied",Result(await Controller().CacheCheck(new CacheCheckInput{hashes=invalid})).Status==400);
        Check("invalid batches do not reach upstream",mock.Calls==0);
        var input=new CacheCheckInput{hashes=new[]{new string('A',32),new string('0',40)}};
        var normalized=Result(await Controller().CacheCheck(input));
        Check("base32 normalization and deduplication",normalized.Status==200 && mock.Hashes.SequenceEqual(new[]{new string('0',40)}));
        mock.Reset();mock.Delay=150;
        var results=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>Controller().CacheCheck(new CacheCheckInput{hashes=hashes})));
        Check("100 hashes accepted and concurrent requests coalesced",mock.Calls==1 && mock.Hashes.Count==100 && results.All(x=>Result(x).Status==200));
        var items=(JArray)Result(results[0]).Json["items"];
        Check("cache hit miss unknown remain distinct",(bool?)items[0]["cached"]==true && (bool?)items[1]["cached"]==false && items[2]["cached"].Type==JTokenType.Null);
        await Controller().CacheCheck(new CacheCheckInput{hashes=hashes.Reverse().ToArray()});
        Check("reordered batch reuses 30 second cache",mock.Calls==1);
        Policy(true);await Controller(key:"other-offline-placeholder-key").CacheCheck(new CacheCheckInput{hashes=hashes});
        Check("cache is isolated by credentials",mock.Calls==2);
        mock.Reset("429");
        var error=Result(await Controller(key:testKey).CacheCheck(input));
        await Controller(key:testKey).CacheCheck(new CacheCheckInput{hashes=new[]{hashes[1]}});
        Check("429 is unknown with account cooldown",(bool?)error.Json["retryable"]==true && error.Json["items"][0]["cached"].Type==JTokenType.Null && mock.Calls==1);
        mock.Reset("malformed");
        Check("malformed cache response remains unknown",(bool?)Result(await Controller(key:testKey).CacheCheck(input)).Json["retryable"]==true);
        mock.Reset();mock.Delay=150;
        var cancelled=Controller(key:testKey);cancelled.HttpContext.RequestAborted=new CancellationToken(true);
        var shared=await Task.WhenAll(cancelled.CacheCheck(input),Controller(key:testKey).CacheCheck(input));
        Check("shared batch survives first caller cancellation",mock.Calls==1 && shared.All(x=>(bool?)Result(x).Json["items"][0]["cached"]==true));
        Check("shared batch carries its own cancellation deadline",mock.HasCancellation.All(x=>x));
        Check("cache checks never mutate TorBox",mock.Mutations==0);
        Policy();
    }

    static async Task DeletionChecks(MockHttp mock) {
        mock.Reset();deleteConfirmations.Clear();
        Check("delete requires explicit confirmation",Result(await Controller().DeleteTorrent(new DeleteInput{id=7})).Status==400 && mock.Calls==0);
        var c=Controller();
        var accepted=Result(await c.DeleteTorrent(new DeleteInput{id=7,confirmed=true})).Json;
        Check("only selected torrent is deleted",mock.Mutations==1 && (long?)mock.Mutation["torrent_id"]==7 && (bool?)mock.Mutation["all"]==false && (string)mock.Mutation["operation"]=="delete");
        Check("accepted mutation is not confirmed deletion",(bool?)accepted["accepted"]==true && (bool?)accepted["deletion_confirmed"]==false);
        await Controller().DeleteTorrent(new DeleteInput{id=7,confirmed=true});
        Check("repeat delete reuses pending ticket",mock.Mutations==1);
        string ticket=(string)accepted["ticket"];
        var check=new DeleteCheckInput{ticket=ticket};int before=mock.Calls;
        await Controller().CheckDeletion(check);
        Check("verification respects one minute delay",mock.Calls==before);
        Check("different account cannot confirm deletion",Result(await Controller(uid:"other").CheckDeletion(check)).Status==403);
        var policy=JObject.Parse(IOFile.ReadAllText(configPath));policy["allowed_uids"]=new JArray(testUid,"other");IOFile.WriteAllText(configPath,policy.ToString());
        Check("another authorized UID cannot use deletion ticket",Result(await Controller(uid:"other").CheckDeletion(check)).Status==410);
        Policy();
        var pending=deleteConfirmations[ticket];pending.CheckAfter=DateTime.UtcNow.AddSeconds(-1);
        libraryData=new JObject{[testUid]=new JObject{["id:7"]=new JObject{["title"]="Offline movie"}}};
        foreach(string mode in new[]{"normal","wrong404","badlist"}) {
            mock.Mode=mode;
            var notGone=Result(await Controller().CheckDeletion(check));
            Check(mode+" does not falsely confirm disappearance",(bool?)notGone.Json["deletion_confirmed"]==false && libraryData[testUid]["id:7"]!=null);
        }
        mock.Mode="missing";
        var confirmed=Result(await Controller().CheckDeletion(check));
        Check("404 ITEM_NOT_FOUND confirms absence",(bool?)confirmed.Json["deletion_confirmed"]==true);
        Check("metadata removed only after confirmed disappearance",libraryData[testUid]["id:7"]==null);
        Check("verification never sends another mutation",mock.Mutations==1);
        deleteConfirmations.Clear();
        var fallback=new DeleteCheckInput{ticket=ticket,id=7,scope=(string)accepted["scope"]};
        libraryData[testUid]["id:7"]=new JObject{["title"]="Keep after lost ticket"};
        Check("lost ticket allows scoped read-only verification",(bool?)Result(await Controller().CheckDeletion(fallback)).Json["deletion_confirmed"]==true && libraryData[testUid]["id:7"]!=null);
        fallback.scope="wrong";
        Check("lost ticket cannot cross credential scope",Result(await Controller().CheckDeletion(fallback)).Status==410);
        mock.Reset("429");
        Check("failed delete is not reported as accepted",Result(await Controller().DeleteTorrent(new DeleteInput{id=7,confirmed=true})).Status==429 && mock.Mutations==0);
        Policy(true);
        Check("changed key cannot confirm old ticket",Result(await Controller(key:"other-offline-placeholder-key").CheckDeletion(check)).Status==410);
        Policy();mock.Reset();
    }

    static async Task ExternalChecks(MockHttp mock) {
        Policy();seriesSessions.Clear();externalPlaylists.Clear();
        var c=Controller();await Probe(c);
        var state=new SeriesSession{Owner=testUid,KeyHash=SeriesKeyHash(testKey),Episodes=new List<SeriesEpisode>{new(){Season=1,Episode=1,Name="One",Pick=new Pick(),File=1}}};
        seriesSessions["offline-session"]=state;
        var input=new ExternalInput{session="offline-session",index=0};
        Check("external playlist creation disabled by default",Result(await c.PrepareExternal(input)).Status==403 && externalPlaylists.IsEmpty);
        var invalidPolicy=JObject.Parse(IOFile.ReadAllText(configPath));invalidPolicy["allow_external_playlists"]="true";IOFile.WriteAllText(configPath,invalidPolicy.ToString());
        Check("external permission requires server boolean",Result(await Controller().PrepareExternal(input)).Status==503);
        Policy(external:true);
        var prepared=Result(await Controller().PrepareExternal(input)).Json;
        string url=(string)prepared["playlist"][0]["url"],access=url.Split('/')[6];
        // URL: https://lampac.example/torbox/series/external/<access>/0.m3u8
        Check("external token has 32 random bytes encoded as 64 hex characters",System.Text.RegularExpressions.Regex.IsMatch(access,"^[a-f0-9]{64}$"));
        Check("relay URL contains no account keys",!url.Contains(testKey) && !url.Contains("offline-parser-key"));
        var playlist=c.GetExternal(access);
        Check("external lifetime has idle and absolute bounds",playlist.Expires>DateTime.UtcNow.AddHours(11) && playlist.Limit<DateTime.UtcNow.AddDays(7).AddSeconds(1));
        playlist.Streams[0]=(DateTime.UtcNow.AddSeconds(30),"https://stream.example/offline");
        var relay=Controller(anonymous:true);
        Check("valid bearer can resolve without client headers",await relay.ExternalSeriesStream(access,0) is RedirectResult);
        Check("relay suppresses caching and referrers",relay.Response.Headers["Cache-Control"]=="no-store" && relay.Response.Headers["Referrer-Policy"]=="no-referrer");
        Policy(external:false);
        Check("administrator can disable existing bearer links",Result(await Controller().ExternalSeriesStream(access,0)).Status==403);
        Policy(external:true);
        var changed=JObject.Parse(IOFile.ReadAllText(configPath));changed["api_key"]="other-offline-placeholder-key";IOFile.WriteAllText(configPath,changed.ToString());
        Check("server key change invalidates bearer links",Result(await Controller().ExternalSeriesStream(access,0)).Status==410);
        Policy(external:true);
        changed=JObject.Parse(IOFile.ReadAllText(configPath));changed["allowed_uids"]=new JArray("other");IOFile.WriteAllText(configPath,changed.ToString());
        Check("server UID removal invalidates bearer links",Result(await Controller().ExternalSeriesStream(access,0)).Status==403);
        Policy(external:true);playlist.Expires=DateTime.UtcNow.AddSeconds(-1);
        Check("idle expiration invalidates bearer",Result(await Controller().ExternalSeriesStream(access,0)).Status==410);
        var another=Result(await Controller().PrepareExternal(input)).Json;
        string access2=((string)another["playlist"][0]["url"]).Split('/')[6];
        Check("new playlists receive different tokens",access!=access2);
        var p2=c.GetExternal(access2);p2.Limit=DateTime.UtcNow.AddSeconds(-1);
        Check("absolute expiration overrides renewed idle time",Result(await Controller().ExternalSeriesStream(access2,0)).Status==410);
        var third=Result(await Controller().PrepareExternal(input)).Json;
        string access3=((string)third["playlist"][0]["url"]).Split('/')[6];
        Policy(true,external:true);
        var cp=c.GetExternal(access3);cp.ServerMode=false;cp.ClientKey=testKey;
        cp.State.Owner=testUid+":torbox:"+SeriesKeyHash(testKey).ToLowerInvariant();
        Check("anonymous revocation denied",Result(await Controller(anonymous:true,key:testKey).RevokeExternal()).Status==403);
        await Controller(uid:"other",key:testKey).RevokeExternal();
        Check("revocation cannot affect other UID",!externalPlaylists.IsEmpty);
        var second=new ExternalPlaylist{Uid=testUid,ClientKey=testKey,State=new SeriesSession()};
        second.Streams[0]=(DateTime.UtcNow.AddSeconds(30),"https://stream.example/second");
        externalPlaylists["offline-second-ticket"]=second;
        cp.Streams[0]=(DateTime.UtcNow.AddSeconds(30),"https://stream.example/offline");
        await cp.State.Gate.WaitAsync();
        Task<ActionResult> revoke=null,queuedRelay=null;
        bool completedWhileBusy=false,allRevokedWhileBusy=false;
        try {
            // Hold the actual relay gate; no scheduling delay is used to assume revocation.
            queuedRelay=Controller(anonymous:true).ExternalSeriesStream(access3,0);
            Check("relay is queued behind the playlist gate",!queuedRelay.IsCompleted);
            revoke=Controller(key:"changed-offline-placeholder-key").RevokeExternal();
            completedWhileBusy=await Task.WhenAny(revoke,Task.Delay(2000))==revoke;
            allRevokedWhileBusy=cp.Revoked && second.Revoked && externalPlaylists.IsEmpty &&
                cp.ClientKey==null && second.ClientKey==null && cp.Streams.IsEmpty && second.Streams.IsEmpty;
            // Simulate a resolver finishing after the initial cache cleanup.
            cp.Streams[0]=(DateTime.UtcNow.AddSeconds(30),"https://stream.example/late");
        }finally{cp.State.Gate.Release();}
        var revoked=Result(await revoke);
        Check("revocation completes while a relay gate is held",completedWhileBusy);
        Check("busy playlist cannot delay other playlist revocation or credential cleanup",allRevokedWhileBusy);
        Check("owner can revoke after personal key change",(int?)revoked.Json["revoked"]==2);
        Check("queued relay rechecks revocation after acquiring gate",Result(await queuedRelay).Status==410);
        Check("revoked relay leaves no cached stream URLs",cp.Streams.IsEmpty);
        Check("revoked bearer returns gone",Result(await Controller().ExternalSeriesStream(access3,0)).Status==410);
        Check("external checks used no upstream network",mock.Calls==0);
    }

    static void LogChecks() {
        string token=new string('a',64),key="offline-sensitive-key";
        string raw="https://lampac.example/prefix/torbox/series/external/"+token+"/1.m3u8?x=1\nhttps://parser.example/?apikey="+key+"&title=Movie";
        string clean=SensitiveLog.Redact(raw);
        Check("log redaction covers bearer paths and JacRed query keys",!clean.Contains(token) && !clean.Contains(key) && clean.Contains("title=Movie"));
        Check("redaction happens before URI truncation",!SensitiveLog.Redact(new string('x',2020)+raw).Substring(0,2048).Contains(new string('a',10)));
        Check("redaction is case insensitive",!SensitiveLog.Redact(raw.ToUpperInvariant()).Contains(key.ToUpperInvariant()));
        Check("ordinary log URLs remain readable",SensitiveLog.Redact("/torbox/list?offset=100")=="/torbox/list?offset=100");
    }
}
