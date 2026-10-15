
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

public class EventSender : UdonSharpBehaviour
{ 
    public UdonSharpBehaviour[] listeners;

    public override void Interact()
    {
        for (int i = 0; i < listeners.Length; i++)
        {
            listeners[i].SendCustomNetworkEvent(NetworkEventTarget.All, "OnEventSender");
        }
    }
}
