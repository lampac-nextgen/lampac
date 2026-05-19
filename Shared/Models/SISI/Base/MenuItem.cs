namespace Shared.Models.SISI.Base;

public class MenuItem
{
    public MenuItem() { }

    public MenuItem(string title, string playlist_url)
    {
        this.title = title;
        this.playlist_url = playlist_url;
    }

    public string title { get; set; }

    public string search_on { get; set; }

    public string playlist_url { get; set; }

    public List<MenuItem> submenu { get; set; }
}
