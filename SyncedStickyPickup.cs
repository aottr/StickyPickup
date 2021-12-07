/*
 * Copyright 2021 AlexOttr <alex@ottr.one>
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the
 * "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, 
 * distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to 
 * the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF 
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY 
 * CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE 
 * SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Enums;

namespace OttrOne.StickyPickup
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class SyncedStickyPickup : UdonSharpBehaviour
    {
        [Header("SyncedStickyPickup v1.2.1")]
        [SerializeField, Tooltip("Root bone for the Sticky Pickup.")]
        private HumanBodyBones Bone;
        [SerializeField, Tooltip("Radius of the spherical tracking area."), Range(0.001f, 4f)]
        private float Radius = 0.3f;
        [SerializeField, Tooltip("Set if Sticky Pickup only works for player in VR.")]
        private bool OnlyVR;
        [SerializeField, Tooltip("Dropping the pickup inside the radius will attach it.")]
        private bool PlaceOnDrop = false;


        [Header("Reset Options")]
        [SerializeField, Tooltip("Using a legacy remote script for the reset on busy worlds.")]
        private bool UseAlternativeColliderCheck = false;
        [SerializeField, Tooltip("Trigger collider as a cage for the pickup.")]
        private Collider AreaCollider;
        [SerializeField, Tooltip("Reset height will be ignored if area collider is given.")]
        private float ResetHeight = -50;

        [Header("Debug Options - Disable in productive world")]
        [SerializeField, Tooltip("Expose information to debug console.")]
        private bool DebugMode = false;

        private bool localStickedOn = false;
        private bool localPickedUp = false;

        private Vector3 _origPos;
        private Quaternion _origRot;

        private bool wasKinematic;

        private Rigidbody rigidBody;
        private VRC_Pickup pickup;

        [UdonSynced, HideInInspector]
        public int BoneIndex; // couters idle since its either attached to hand or body bone
        [UdonSynced, HideInInspector]
        public Vector3 objectPosOffset;
        [UdonSynced, HideInInspector]
        public Quaternion objectRotOffset;

        public void Start()
        {
            if (DebugMode) Debug.Log($"[StickyPickup] started for {gameObject.name}");

            this.rigidBody = (Rigidbody)gameObject.GetComponent(typeof(Rigidbody));
            this.pickup = (VRC_Pickup)gameObject.GetComponent(typeof(VRC_Pickup));
            this.wasKinematic = rigidBody.isKinematic;
            BoneIndex = -1;

            if (!PlaceOnDrop) this.pickup.AutoHold = VRC_Pickup.AutoHoldMode.Yes;

            _origPos = new Vector3(rigidBody.transform.position.x, rigidBody.transform.position.y, rigidBody.transform.position.z);
            _origRot = new Quaternion(rigidBody.transform.rotation.x, rigidBody.transform.rotation.y, rigidBody.transform.rotation.z, rigidBody.transform.rotation.w);
        }

        /// <summary>
        /// Resets the position, bone and rotation of the item
        /// </summary>
        public void ResetPosition()
        {
            pickup.Drop();
            rigidBody.velocity = Vector3.zero;
            rigidBody.angularVelocity = Vector3.zero;
            BoneIndex = -1;
            localStickedOn = false;
            localPickedUp = false;

            SetKinematic(this.wasKinematic);

            this.rigidBody.transform.SetPositionAndRotation(_origPos, _origRot);
            if (DebugMode) Debug.Log($"[StickyPickup] Requested Reset for {gameObject.name}");
        }

        private void SetKinematic(bool value)
        {
            // disabling the object (workaround for triggering the collider) will reset velo
            Vector3 velo = rigidBody.velocity;
            Vector3 angyVelo = rigidBody.angularVelocity;

            this.gameObject.SetActive(false);
            rigidBody.isKinematic = value;
            this.gameObject.SetActive(true);

            rigidBody.velocity = velo;
            rigidBody.angularVelocity = angyVelo;

            if (DebugMode) Debug.Log($"[StickyPickup] Set {gameObject.name} kinematic to {value}");
        }

        public void ResetPositionAllPlayers()
        {
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "ResetPosition");
        }

        /// <summary>
        /// Called when the object gets picked up. It will set the owner to the perso who picked it up,
        /// set the global variables and disable the gravity effect
        /// </summary>
        public override void OnPickup()
        {
            Networking.SetOwner(Networking.LocalPlayer, this.gameObject);
            // synced variables
            BoneIndex = pickup.currentHand == VRC_Pickup.PickupHand.Left ? (int)HumanBodyBones.LeftHand : (int)HumanBodyBones.RightHand;
            CalculateOffsets((HumanBodyBones)this.BoneIndex);

            localStickedOn = false;
            localPickedUp = true;

            if (!rigidBody.isKinematic && !this.wasKinematic) // is gravity item
            {
                SetKinematic(true);
            }

            RequestSerialization();
        }

        /// <summary>
        /// Calculates the positional and rotational offset between the selected bone and the object on pickup / placing
        /// </summary>
        /// <param name="bone">Humanoid bone the item is attached to</param>
        private void CalculateOffsets(HumanBodyBones bone)
        {
            Vector3 objPos = this.rigidBody.transform.position;
            Vector3 plyPos = Networking.LocalPlayer.GetBonePosition(bone);
            Quaternion invRot = Quaternion.Inverse(Networking.LocalPlayer.GetBoneRotation(bone));
            // q^(-1) * Vector (x2-x1, y2-y1, z2-z1)
            objectPosOffset = invRot * (objPos - plyPos);
            // calculate the rotation by multiplying the current rotation with inverse player rotation
            objectRotOffset = invRot * this.rigidBody.transform.rotation;
        }

        /// <summary>
        /// Drop calculates the current offset to the selected bone. If its within the radius it assumes that the player wants to place
        /// the item there so it attaches it to the player.
        /// 
        /// If its outside the radius it will put it into idle state.
        /// </summary>
        public override void OnDrop()
        {
            if (PlaceOnDrop) TryAttach();
            
            if (localPickedUp) // still true if try was not successful
            {
                BoneIndex = -1;
                localPickedUp = false;
            }
            RequestSerialization();
        }

        public override void OnPickupUseUp()
        {
            if (PlaceOnDrop) return;

            TryAttach();
            // successful try will set localStickedOn to true
            if (localStickedOn) RequestSerialization();
        }

        private void TryAttach()
        {
            Vector3 objPos = this.rigidBody.transform.position;
            Vector3 plyPos = Networking.LocalPlayer.GetBonePosition(Bone);

            if (Vector3.Distance(objPos, plyPos) <= Radius && (Networking.LocalPlayer.IsUserInVR() || !OnlyVR))
            {
                BoneIndex = (int)Bone;

                localStickedOn = true;
                localPickedUp = false;
                CalculateOffsets(Bone);
                if (!PlaceOnDrop) pickup.Drop();
                if (DebugMode) Debug.Log($"[StickyPickup] Attached {gameObject.name}");
            }
        }

        /// <summary>
        /// Main function to synchronize Position and states between the clients.
        /// </summary>
        public override void PostLateUpdate()
        {
            if (localStickedOn)
            {
                Vector3 bonePosition = Networking.LocalPlayer.GetBonePosition(Bone);
                Quaternion boneRotation = Networking.LocalPlayer.GetBoneRotation(Bone);

                // calculate the wanted offset by multiplying the bonerotation with the position offset and add this to the targeted bone position
                // calculate the wanted rotation by multiplying the current bone rotation with the calculated rotation offset 
                this.rigidBody.transform.SetPositionAndRotation((boneRotation * objectPosOffset) + bonePosition, boneRotation * objectRotOffset);
            }
            else if (localPickedUp)
            {
                CalculateOffsets((HumanBodyBones)this.BoneIndex);
                RequestSerialization();
            }
            else if (this.BoneIndex >= 0)
            {
                Vector3 bonePosition = Networking.GetOwner(gameObject).GetBonePosition((HumanBodyBones)this.BoneIndex);
                Quaternion boneRotation = Networking.GetOwner(gameObject).GetBoneRotation((HumanBodyBones)this.BoneIndex);
                if (!rigidBody.isKinematic && !this.wasKinematic) // is gravity item
                {
                    SetKinematic(true);
                }

                this.rigidBody.transform.SetPositionAndRotation((boneRotation * objectPosOffset) + bonePosition, boneRotation * objectRotOffset);
            }
            else
            {
                // idle gravity mode -> sync position till next pickup
                if (objectPosOffset != rigidBody.transform.position)
                {
                    if (Networking.GetOwner(gameObject) == Networking.LocalPlayer)
                    {
                        this.objectPosOffset = rigidBody.transform.position;
                        this.objectRotOffset = rigidBody.transform.rotation;
                        RequestSerialization();
                    }
                    else
                    {
                        rigidBody.transform.SetPositionAndRotation(objectPosOffset, objectRotOffset);
                    }
                }
                if (rigidBody.isKinematic && !this.wasKinematic)
                {
                    SetKinematic(false);
                }
            }

            if (AreaCollider == null && UseAlternativeColliderCheck == false && rigidBody.transform.position.y < ResetHeight) ResetPositionAllPlayers();
        }

        private void OnTriggerExit(Collider collider)
        {
            if (UseAlternativeColliderCheck) return;
            if (DebugMode) Debug.Log($"[StickyPickup] {gameObject.name} Exiting collider {collider}");
            if (collider == AreaCollider)
            {
                SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "ResetPosition");
            }
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            BoneIndex = -1;
            pickup.Drop();
            localStickedOn = false;
            localPickedUp = false; // in case it gets stolen from the hands
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (DebugMode) Debug.Log($"[StickyPickup] {gameObject.name} will be synchronized for late joining players.");
            if (Networking.GetOwner(gameObject) == Networking.LocalPlayer)
            {
                SendCustomEventDelayedSeconds("SyncLatePlayer", 2.5F, EventTiming.Update);
            }  
        }

        public void SyncLatePlayer()
        {
            RequestSerialization();
        }
    }
}
