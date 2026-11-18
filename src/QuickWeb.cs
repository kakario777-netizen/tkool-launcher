using System;
using System.Net;

namespace TkoolLauncher
{
    /// <summary>응답이 없으면 오래 붙잡지 않는 WebClient (기본 100초 → 12초)</summary>
    class QuickWeb : WebClient
    {
        public int TimeoutMs = 12000;
        protected override WebRequest GetWebRequest(Uri address)
        {
            var r = base.GetWebRequest(address);
            if (r != null) { r.Timeout = TimeoutMs; var h = r as HttpWebRequest; if (h != null) h.ReadWriteTimeout = TimeoutMs; }
            return r;
        }
    }
}
