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
        [UdonSynced, FieldChangeCallback(nameof(SyncedBoneIndex)), HideInInspector]
        public int BoneIndex; // couters idle since its either attached to hand or body bone
        public int SyncedBoneIndex
        {
            set
            {
                localStickedOn = false;
                localPickedUp = false; // in case it gets stolen from the hands
                BoneIndex = value;
            }
            get => BoneIndex;
        }
        [Header("SyncedStickyPickup v1.2.0 Beta")]
        [Tooltip("Root bone for the Sticky Pickup.")]
        public HumanBodyBones Bone;
        [Tooltip("Radius of the spherical tracking area."), Range(0.001f, 4f)]
        public float Radius = 0.3f;
        [Tooltip("Set if Sticky Pickup only works for player in VR.")]
        public bool OnlyVR;
        [Tooltip("Dropping the pickup inside the radius will attach it.")]
        public bool PlaceOnDrop = false;


        [Header("Reset Options")]
        [Tooltip("Trigger collider as a cage for the pickup.")]
        public Collider AreaCollider;
        [Tooltip("Reset height will be ignored if area collider is given.")]
        public float ResetHeight;

        private bool localStickedOn = false;
        private bool localPickedUp = false;

        private Vector3 _origPos;
        private Quaternion _origRot;

        private bool wasKinematic;

        private Rigidbody rigidBody;
        private VRC_Pickup pickup;

        [UdonSynced, HideInInspector]
        public Vector3 objectPosOffset;
        [UdonSynced, HideInInspector]
        public Quaternion objectRotOffset;

        public void Start()
        {
            this.rigidBody = (Rigidbody)gameObject.GetComponent(typeof(Rigidbody));
            this.pickup = (VRC_Pickup)gameObject.GetComponent(typeof(VRC_Pickup));
            this.wasKinematic = rigidBody.isKinematic;
            this.BoneIndex = -1;

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
            SyncedBoneIndex = -1;
            this.gameObject.SetActive(false);
            rigidBody.isKinematic = this.wasKinematic;
            this.gameObject.SetActive(true);
            this.rigidBody.transform.SetPositionAndRotation(_origPos, _origRot);
            Debug.Log($"Requested Reset for {gameObject.name}");
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
            Debug.Log($"Pickup");
            // synced variables
            SyncedBoneIndex = pickup.currentHand == VRC_Pickup.PickupHand.Left ? (int)HumanBodyBones.LeftHand : (int)HumanBodyBones.RightHand;
            CalculateOffsets((HumanBodyBones)this.BoneIndex);

            localStickedOn = false;
            localPickedUp = true;
            if (!rigidBody.isKinematic && !this.wasKinematic) // is gravity item
            {
                this.gameObject.SetActive(false);
                rigidBody.isKinematic = true;
                this.gameObject.SetActive(true);
                Debug.Log($"Enabled Kinematic");
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
            if (PlaceOnDrop)
            {
                Attach();
            }
            else if (localPickedUp)
            {
                SyncedBoneIndex = -1;
                localPickedUp = false;
            }
            RequestSerialization();
        }

        public override void OnPickupUseUp()
        {
            if (PlaceOnDrop) return;

            Attach();
            RequestSerialization();
        }

        private void Attach()
        {
            Vector3 objPos = this.rigidBody.transform.position;
            Vector3 plyPos = Networking.LocalPlayer.GetBonePosition(Bone);

            if (Vector3.Distance(objPos, plyPos) <= Radius && (Networking.LocalPlayer.IsUserInVR() || !OnlyVR))
            {
                SyncedBoneIndex = (int)Bone;

                localStickedOn = true;
                localPickedUp = false;
                CalculateOffsets(Bone);
            }
            else
            {
                SyncedBoneIndex = -1;
                localPickedUp = false;
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
                    this.gameObject.SetActive(false);
                    rigidBody.isKinematic = true;
                    this.gameObject.SetActive(true);
                    Debug.Log($"Enabled Kinematic");
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
                    this.gameObject.SetActive(false);
                    rigidBody.isKinematic = false;
                    this.gameObject.SetActive(true);
                    Debug.Log($"Disabled Kinematic");
                }
            }

            if (AreaCollider == null && rigidBody.transform.position.y < ResetHeight) ResetPositionAllPlayers();
        }

        private void OnTriggerExit(Collider collider)
        {
            if (collider == AreaCollider)
            {
                SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "ResetPosition");
            }
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            SendCustomEventDelayedSeconds("SyncLatePlayer", 2.5F, EventTiming.Update);
        }

        public void SyncLatePlayer()
        {
            RequestSerialization();
        }
    }
}
