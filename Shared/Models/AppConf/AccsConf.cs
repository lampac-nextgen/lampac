using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using System.Collections.Concurrent;

namespace Shared.Models.AppConf;

public class AccsConf
{
    public bool enable { get; set; }

    public string shared_passwd { get; set; }

    public int shared_daytime { get; set; }

    public string whitepattern { get; set; }

    public HashSet<string> white_uids { get; set; }

    public string domainId_pattern { get; set; }

    public int maxip_hour { get; set; }

    public int maxrequest_hour { get; set; }

    public int maxlock_day { get; set; }

    public int blocked_hour { get; set; }

    public string authMesage { get; set; }

    public string denyMesage { get; set; }

    public string denyGroupMesage { get; set; }

    public string expiresMesage { get; set; }

    public Dictionary<string, object> @params { get; set; }

    public Dictionary<string, DateTime> accounts { get; set; } = new Dictionary<string, DateTime>();

    public ConcurrentBag<AccsUser> users { get; set; } = new ConcurrentBag<AccsUser>();


    private static IReadOnlyDictionary<string, AccsUser> _searchUsers;

    public void RefreshUsers()
    {
        try
        {
            if (users == null || users.Count == 0)
            {
                _searchUsers = null;
                return;
            }

            Dictionary<string, AccsUser> _users = new();

            foreach (AccsUser u in users)
            {
                if (!string.IsNullOrEmpty(u.id))
                    _users[u.id.ToLowerAndTrim()] = u;

                if (u.ids != null)
                {
                    foreach (string uid in u.ids)
                    {
                        if (!string.IsNullOrEmpty(uid))
                            _users[uid.ToLowerAndTrim()] = u;
                    }
                }
            }

            _searchUsers = _users;
        }
        catch { }
    }

    public AccsUser findUser(HttpContext httpContext, out string uid)
    {
        if (users == null || users.Count == 0)
        {
            uid = null;
            return null;
        }

        var user = findUser(httpContext.Request.Query["token"]) ??
                   findUser(httpContext.Request.Query["account_email"]) ??
                   findUser(httpContext.Request.Query["uid"]) ??
                   findUser(httpContext.Request.Query["box_mac"]);

        if (user != null)
        {
            uid = user.id;
            return user;
        }

        uid = null;
        return null;
    }

    public AccsUser findUser(StringValues uid)
    {
        if (uid.Count == 0 || _searchUsers == null || _searchUsers.Count == 0)
            return null;

        uid = uid[0].ToLowerAndTrim();
        if (string.IsNullOrEmpty(uid))
            return null;

        if (_searchUsers.TryGetValue(uid, out AccsUser _user))
            return _user;

        return null;
    }
}
