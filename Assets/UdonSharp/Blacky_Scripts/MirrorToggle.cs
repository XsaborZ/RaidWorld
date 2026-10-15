
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class MirrorToggle : UdonSharpBehaviour
{
    [Header("Items")] 
    [SerializeField] public GameObject MirrorObject;

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (!player.isLocal) return;
        MirrorObject.SetActive(true);
    }

    public override void OnPlayerTriggerExit(VRCPlayerApi player)
    {
        if (!player.isLocal) return;
        MirrorObject.SetActive(false);
    }
}
