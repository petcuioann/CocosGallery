using System;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;

namespace CocosGallery
{
    public static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();

            bool isRedirect = DecideRedirection();
            if (!isRedirect)
            {
                Microsoft.UI.Xaml.Application.Start((p) =>
                {
                    var context = new DispatcherQueueSynchronizationContext(
                        DispatcherQueue.GetForCurrentThread());
                    SynchronizationContext.SetSynchronizationContext(context);
                    new App();
                });
            }
        }

        // pre: app is starting up
        // post: determines if this instance should redirect to an existing main instance
        // except: silences exceptions if registration fails and falls back to normal start
        // complexity: O(1) for instance checking
        private static bool DecideRedirection()
        {
            bool isRedirect = false;
            try
            {
                var mainInstance = AppInstance.FindOrRegisterForKey("CocosGalleryMainInstance");
                if (!mainInstance.IsCurrent)
                {
                    isRedirect = true;
                    var activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
                    mainInstance.RedirectActivationToAsync(activatedArgs).AsTask().Wait();
                }
                else
                {
                    mainInstance.Activated += MainInstance_Activated;
                }
            }
            catch (Exception)
            {
            }
            return isRedirect;
        }

        private static void MainInstance_Activated(object? sender, AppActivationArguments e)
        {
            if (App.Instance is App app)
            {
                app.OnActivatedByInstance(e);
            }
        }
    }
}
