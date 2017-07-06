using Microsoft.AspNet.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace reactive_download.Helper
{
    public class NotificationHub : Hub
    {

        private Timer reactiveTimer;

        private event TimerCallback callback = new TimerCallback((callback) =>
        {
            (callback as Action)?.Invoke();
        });
        public void notifyClientOnFileDownloaded(object inputParameter)
        {
            Clients.Caller.notify_on_fileDownloaded(inputParameter);
        }

        public void NotificationHubInitilization()
        {
            if (Context == null || Context?.User?.Identity.IsAuthenticated == false)
                return;
            Action Ping = () =>
            {
                try
                {
                    Clients.Caller.notifyClient_on_maintenance(this.Context.ConnectionId, new { status = "start" });
                }
                catch (Exception cacheEx)
                {
                    //  CommonItems.Logger?.ErrorException(cacheEx, "Error occurred while getting user notifications from the cache.");
                }

            };
            reactiveTimer = new Timer(callback, Ping, 1000, 10000);
        }

        public override Task OnConnected()
        {
            Action<ClaimsPrincipal> notifyClientOnLogin = (userClaim) =>
            {
                Clients.Caller.notifyClient_on_login_callback(userClaim.Identity.Name, Utilities.GetHashToken(userClaim));
            };
            try
            {
                var user = HttpContext.Current.GetOwinContext().Authentication.User;
                NotificationHubInitilization();
                notifyClientOnLogin(user);
            }
            catch (Exception ex) when (!(ex is UiException))
            {
                // CommonItems.Logger?.ErrorException(ex, "Error occurred while getting user notifications.");
                return null;
            }
            return base.OnConnected();

        }
        public override Task OnDisconnected(bool stopCalled)
        {
            return base.OnDisconnected(stopCalled);
        }
        public override Task OnReconnected()
        {
            return base.OnReconnected();
        }
       

    }
}