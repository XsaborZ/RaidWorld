
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class OnPlayerToggle : UdonSharpBehaviour
{
    //private BoxCollider parentCollider;
    public GameObject[] gameObjects;

    void Start()
    {
        for (int i = 0; i < gameObjects.Length; i++)
        {
            gameObjects[i].SetActive(false);
        }
    }
    
    public override void OnPlayerTriggerEnter(VRCPlayerApi player) 
    {
        if (!player.isLocal) return;
        
        for (int i = 0; i < gameObjects.Length; i++)
        {
            gameObjects[i].SetActive(true);
        }
    }

    public override void OnPlayerTriggerExit(VRCPlayerApi player)
    {
        if (!player.isLocal) return;

        for (int i = 0; i < gameObjects.Length; i++)
        {
            gameObjects[i].SetActive(false);
        }
    }
}
