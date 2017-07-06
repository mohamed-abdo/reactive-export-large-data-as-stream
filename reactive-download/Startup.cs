using Microsoft.AspNet.SignalR;
using Owin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace reactive_download
{
    public class Startup
    {
        public void Configuration(IAppBuilder app)
        {

            #region signalR
            //singalr middleware
            app.MapSignalR();
            //require authentication for calling signalr
            GlobalHost.HubPipeline.RequireAuthentication();
            // Make long polling connections wait a maximum of 300 seconds for a
            // response. When that time expires, trigger a timeout command and
            // make the client reconnect.
            GlobalHost.Configuration.ConnectionTimeout = TimeSpan.FromSeconds(300);
            // Wait a maximum of 250 seconds after a transport connection is lost
            // before raising the Disconnected event to terminate the SignalR connection.
            GlobalHost.Configuration.DisconnectTimeout = TimeSpan.FromSeconds(240);
            // For transports other than long polling, send a keepalive packet every
            // This value must be no more than 1/3 of the DisconnectTimeout value.
            GlobalHost.Configuration.KeepAlive = TimeSpan.FromSeconds(80);
            GlobalHost.Configuration.LongPollDelay = TimeSpan.FromSeconds(5);
            #endregion
        }
    }
}