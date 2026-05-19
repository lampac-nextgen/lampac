namespace Shared.Models.Base;

public class HeadersModel
{
    public HeadersModel(string name, string val)
    {
        this.name = name;
        this.val = val;
    }

    public string name { get; set; }

    public string val { get; set; }


    #region Init
    public static List<HeadersModel> Init(string name, string val)
    {
        if (string.IsNullOrEmpty(val))
            return new List<HeadersModel>();

        return new List<HeadersModel>()
        {
            new(name, val)
        };
    }

    public static List<HeadersModel> Init(List<HeadersModel> headers)
    {
        return headers ?? new List<HeadersModel>();
    }

    public static List<HeadersModel> Init(params (string name, string val)[] headers)
    {
        if (headers == null || headers.Length == 0)
            return new List<HeadersModel>();

        var h = new List<HeadersModel>(headers.Length);

        foreach (var i in headers)
        {
            if (!string.IsNullOrEmpty(i.val))
                h.Add(new(i.name, i.val));
        }

        return h;
    }

    public static List<HeadersModel> Init(IReadOnlyDictionary<string, string> defaultHeaders, params (string name, string val)[] headers)
    {
        if (defaultHeaders == null || defaultHeaders.Count == 0)
            return Init(headers);

        var result = new List<HeadersModel>(defaultHeaders.Count + (headers?.Length ?? 0));

        foreach (var h in defaultHeaders)
        {
            if (!string.IsNullOrEmpty(h.Value))
                result.Add(new(h.Key, h.Value));
        }

        if (headers != null)
        {
            foreach (var h in headers)
            {
                if (!string.IsNullOrEmpty(h.val))
                    result.Add(new(h.name, h.val));
            }
        }

        return result;
    }
    #endregion

    #region Join
    public static List<HeadersModel> Join(List<HeadersModel> h1, List<HeadersModel> h2)
    {
        if (h1 == null || h1.Count == 0)
            return h2 ?? h1 ?? new List<HeadersModel>();

        if (h2 == null || h2.Count == 0)
            return h1;

        var result = new List<HeadersModel>(h1.Count + h2.Count);

        foreach (var h in h1)
        {
            if (!string.IsNullOrEmpty(h.val))
                result.Add(new(h.name, h.val));
        }

        foreach (var h in h2)
        {
            if (!string.IsNullOrEmpty(h.val))
                result.Add(new(h.name, h.val));
        }

        return result;
    }

    public static List<HeadersModel> Join(List<HeadersModel> h1, IReadOnlyDictionary<string, string> h2)
    {
        if (h2 == null || h2.Count == 0)
            return h1 ?? new List<HeadersModel>();

        if (h1 == null || h1.Count == 0)
            return Init(h2);

        var result = new List<HeadersModel>(h1.Count + h2.Count);

        foreach (var h in h1)
        {
            if (!string.IsNullOrEmpty(h.val))
                result.Add(new(h.name, h.val));
        }

        foreach (var h in h2)
        {
            if (!string.IsNullOrEmpty(h.Value))
                result.Add(new(h.Key, h.Value));
        }

        return result;
    }
    #endregion

    #region Join IReadOnly
    public static IReadOnlyList<HeadersModel> JoinReadOnly(IReadOnlyList<HeadersModel> h1, IReadOnlyList<HeadersModel> h2)
    {
        if (h1 == null || h1.Count == 0)
            return h2 ?? h1 ?? new List<HeadersModel>();

        if (h2 == null || h2.Count == 0)
            return h1;

        var result = new List<HeadersModel>(h1.Count + h2.Count);

        foreach (var h in h1)
        {
            if (!string.IsNullOrEmpty(h.val))
                result.Add(new(h.name, h.val));
        }

        foreach (var h in h2)
        {
            if (!string.IsNullOrEmpty(h.val))
                result.Add(new(h.name, h.val));
        }

        return result;
    }
    #endregion

    #region InitOrNull
    public static List<HeadersModel> InitOrNull(IReadOnlyDictionary<string, string> headers)
    {
        if (headers == null || headers.Count == 0)
            return null;

        var h = new List<HeadersModel>(headers.Count);

        foreach (var i in headers)
        {
            if (!string.IsNullOrEmpty(i.Value))
                h.Add(new(i.Key, i.Value));
        }

        if (h.Count == 0)
            return null;

        return h;
    }
    #endregion
}
