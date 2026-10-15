
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class Tester : UdonSharpBehaviour
{
    public GameObject thisObj;
    public void spawnIn()
    {
        Object.Instantiate(thisObj, transform);
    }

    public override void Interact()
    {
        spawnIn();
    }
}
