using System;
using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

namespace Blacky.Movement
{
    public class PlayerStats : UdonSharpBehaviour
    {
        [SerializeField] public Slider MovementSlider;
        [SerializeField] public Slider GravitySlider;
        [SerializeField] public TMP_Text MovementAmmount;
        [SerializeField] public TMP_Text GravityAmmount;
        [SerializeField] public float ScaleFactor = 5;
        private float RunSpeedDefault = 4;
        private float WalkSpeedDefault = 2;
        private float StrafeSpeedDefault = 2;
        private float GravityDefault;
        private VRCPlayerApi localPlayer;
        
        public void Start()
        {
            localPlayer = Networking.LocalPlayer;
            if (localPlayer == null) return;

            RunSpeedDefault = localPlayer.GetRunSpeed();
            WalkSpeedDefault = localPlayer.GetWalkSpeed();
            StrafeSpeedDefault = localPlayer.GetStrafeSpeed();
            GravityDefault = localPlayer.GetGravityStrength();

            MovementAmmount.text = MovementSlider.value.ToString();
            GravityAmmount.text = GravitySlider.value.ToString();
        }

        public void UpdatePlayerStats()
        {
            localPlayer.SetRunSpeed(RunSpeedDefault * Mathf.Pow(ScaleFactor, MovementSlider.value) / ScaleFactor);
            Debug.Log("Run: " + localPlayer.GetRunSpeed());
            localPlayer.SetWalkSpeed(WalkSpeedDefault * Mathf.Pow(ScaleFactor, MovementSlider.value) / ScaleFactor);
            Debug.Log("Walk: " + localPlayer.GetWalkSpeed());
            localPlayer.SetStrafeSpeed(StrafeSpeedDefault * Mathf.Pow(ScaleFactor, MovementSlider.value) / ScaleFactor);
            Debug.Log("Strafe: " + localPlayer.GetStrafeSpeed());

            if (GravitySlider.value == GravitySlider.minValue)
            {
                localPlayer.SetGravityStrength(0);
                Debug.Log("Gravity: " + localPlayer.GetGravityStrength());
            }
            else
            {
                localPlayer.SetGravityStrength(GravityDefault * Mathf.Pow(ScaleFactor, GravitySlider.value) / ScaleFactor);
                Debug.Log("Gravity: " + localPlayer.GetGravityStrength());
            }
            MovementAmmount.text = MovementSlider.value.ToString();
            GravityAmmount.text = GravitySlider.value.ToString();
        } 
    }
}