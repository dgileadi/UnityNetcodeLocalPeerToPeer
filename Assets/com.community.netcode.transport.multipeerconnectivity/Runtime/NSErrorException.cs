using System;

namespace Netcode.Transports.MultipeerConnectivity
{
    public class NSErrorException : Exception
    {
        public NSErrorException(long code, string description)
        : base($"NSError {code}: {description}")
        {
            Code = code;
            Description = description;
        }

        public long Code { get; private set; }

        public string Description { get; private set; }
    }
}
