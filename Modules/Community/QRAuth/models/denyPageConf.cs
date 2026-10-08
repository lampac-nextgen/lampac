namespace QRAuth.Models
{
    public class DenyPageConf
    {
        public string tg_target { get; set; } = "";
        public bool show_qr { get; set; } = true;
        public string page_badge { get; set; } = "";
        public string page_title { get; set; } = "";
        public string page_subtitle { get; set; } = "";
        public string step1_text { get; set; } = "";
        public string step2_text { get; set; } = "";
        public string qr_caption { get; set; } = "";
        public string qr_subcaption { get; set; } = "";
        public string tg_button_text { get; set; } = "";
        public bool poster_wall { get; set; } = true;
        public string poster_source { get; set; } = "trending";
    }
}
