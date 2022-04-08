using System;
using System.Collections.Generic;
using System.Text;

namespace SignalWire.Relay.Calling
{
    public enum CallReferState
    {
        inProgress, 
        cancel, 
        busy, 
        noAnswer, 
        error,
        finished,
    }
}
