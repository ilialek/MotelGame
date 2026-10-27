using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Unity.Services.Core;
using Unity.Services.Authentication;
using UnityEngine;

namespace MotelGame.Networking
{
    // A persistent UGS profile per local process. The OS releases the lease even after a crash.
    public sealed class OnlineIdentity : IDisposable
    {
        FileStream lease;
        Task initialization;
        public string Profile { get; private set; }
        public string PlayerId => UnityServices.State == ServicesInitializationState.Initialized ? AuthenticationService.Instance.PlayerId : "";
        public bool Ready => UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsAuthorized;

        public Task InitializeAsync()
        {
            if (Ready) return Task.CompletedTask;
            if (initialization == null || initialization.IsCompleted) initialization = InitializeCoreAsync();
            return initialization;
        }
        async Task InitializeCoreAsync()
        {
            if (lease == null) AcquireProfile();
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync(new InitializationOptions().SetProfile(Profile));
            if (AuthenticationService.Instance.Profile != Profile)
            {
                if (AuthenticationService.Instance.IsSignedIn) AuthenticationService.Instance.SignOut();
                AuthenticationService.Instance.SwitchProfile(Profile);
            }
            if (AuthenticationService.Instance.IsExpired) AuthenticationService.Instance.SignOut();
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
        void AcquireProfile()
        {
            string requested = Argument("-motel-profile");
            if (requested != null && !Regex.IsMatch(requested, "^[A-Za-z0-9_-]{1,30}$"))
                throw new InvalidOperationException("Invalid local test profile.");
            string folder = Path.Combine(Application.persistentDataPath, "ProfileLeases");
            Directory.CreateDirectory(folder);
            for (int i = 1; i <= 16; i++)
            {
                string profile = requested ?? (Application.isEditor ? "editor" : i == 1 ? "player" : "local-" + i);
                FileStream candidate = null;
                try
                {
                    candidate = new FileStream(Path.Combine(folder, profile + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
                    // Mono on macOS does not enforce FileShare.None across processes.
                    // Explicit byte-range locking is enforced by the OS and released after a crash.
                    candidate.Lock(0, 1);
                    lease = candidate;
                    Profile = profile;
                    Debug.Log("[M13] Authentication profile: " + profile);
                    return;
                }
                catch (IOException)
                {
                    candidate?.Dispose();
                    if (requested != null || Application.isEditor)
                        throw new InvalidOperationException("This local profile is already running. Use another playtest launcher.");
                }
            }
            throw new InvalidOperationException("Too many local game instances.");
        }
        public static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
        public void Dispose() { lease?.Dispose(); lease = null; }
    }
}
