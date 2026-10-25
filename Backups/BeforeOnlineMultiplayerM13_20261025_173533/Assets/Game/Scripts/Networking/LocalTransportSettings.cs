using System;
using System.Net;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace MotelGame.Networking
{
    // Development endpoint configuration only. NetworkPlayer never depends on Unity Transport.
    public sealed class LocalTransportSettings : MonoBehaviour
    {
        [SerializeField] UnityTransport transport;
        [SerializeField] string address = "127.0.0.1";
        [SerializeField] ushort port = 7777;
        public string Address { get => address; set => address = value; }
        public ushort Port { get => port; set => port = value; }

        public void Configure(bool host)
        {
            if (!IPAddress.TryParse(address, out _) || port == 0)
                throw new ArgumentException("Enter a valid IP address and port (1–65535).");
            // Bind to all interfaces on host; clients connect to the entered host address.
            transport.SetConnectionData(address, port, host ? "0.0.0.0" : null);
        }
    }
}
