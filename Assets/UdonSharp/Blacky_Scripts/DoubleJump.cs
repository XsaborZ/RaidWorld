using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;

namespace Blacky.Movement.DoubleJump
{
    public class DoubleJump : UdonSharpBehaviour
    {

        [Header("Player")] 
        [SerializeField] public AudioSource JumpSound;
        [SerializeField] public ParticleSystem JumpParticleSystem;

        [Header("Settings")] 
        [SerializeField] public float jumpImpulse = 4.0f;
        [SerializeField] public float DoublejumpVelocityY = 4.0f;

        [Header("Debug")] //TODO: Remove After Development
        [SerializeField] public Slider JumpSlider;
        [SerializeField] public Slider DoubleJumpSlider; 

        [SerializeField] public TMP_Text JumpAmmount;
        [SerializeField] public TMP_Text DoubleAmmount;

        [HideInInspector] public bool _canJumpAgain;
        private VRCPlayerApi localPlayer;

        public void _resetJump()
        {
            _canJumpAgain = true;
        }

        public void Enable()
        {
            _resetJump();
        }

        public void Start()
        {
            localPlayer = Networking.LocalPlayer;
            if (localPlayer == null) return;
            localPlayer.SetJumpImpulse(jumpImpulse);

            _resetJump();

            // //TODO: Remove after Development
            // JumpSlider.value = jumpImpulse;
            // DoubleJumpSlider.value = DoublejumpVelocityY;
            // JumpAmmount.text = JumpSlider.value.ToString();
            // DoubleAmmount.text = DoubleJumpSlider.value.ToString();
        }

        public override void InputMoveHorizontal(float axisPosition, UdonInputEventArgs args)
        {
            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (localPlayer.IsPlayerGrounded() == true) _canJumpAgain = true;
        }
        
        public override void InputMoveVertical(float axisPosition, UdonInputEventArgs args)
        {
            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (localPlayer.IsPlayerGrounded() == true) _canJumpAgain = true;
        }

        public override void InputJump(bool isPressed, UdonInputEventArgs args)
        {
            Vector3 jumpVelocity = new Vector3(0, DoublejumpVelocityY, 0);
            VRCPlayerApi localPlayer = Networking.LocalPlayer;

            if (localPlayer.IsPlayerGrounded() == false)
            {
                if (_canJumpAgain && isPressed)
                {
                    localPlayer.SetVelocity(jumpVelocity);
                    _canJumpAgain = false;
                }
            } 
            else
            {
                SendCustomEventDelayedSeconds("_resetJump", 0.33f);
            }
        }

        //TODO: Remove After Development
        public void UpdateJumpImpulse()
        {
            localPlayer.SetJumpImpulse(JumpSlider.value);
            Debug.Log("Jump: " + localPlayer.GetJumpImpulse());
            JumpAmmount.text = JumpSlider.value.ToString();
        }

        public void UpdateDoublejumpVelocity()
        {
            DoublejumpVelocityY = DoubleJumpSlider.value;
            Debug.Log("Double Jump Velocity: " + DoublejumpVelocityY);
            DoubleAmmount.text = DoubleJumpSlider.value.ToString();
        }
    }
}