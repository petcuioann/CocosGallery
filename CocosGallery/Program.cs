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
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                System.IO.File.WriteAllText("crash_log.txt", e.ExceptionObject.ToString());
            };

            try
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
            catch (Exception ex)
            {
                System.IO.File.WriteAllText("crash_log.txt", ex.ToString());
                throw;
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
                    var redirectTask = mainInstance.RedirectActivationToAsync(activatedArgs).AsTask();
                    System.Threading.Tasks.Task.WaitAny(redirectTask, System.Threading.Tasks.Task.Delay(1000));
                    Environment.Exit(0);
                }
                else
                {
                    mainInstance.Activated += MainInstance_Activated;
                }
            }
            catch (Exception ex)
            {
                System.IO.File.WriteAllText("redirect_error.txt", ex.ToString());
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
