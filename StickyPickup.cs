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

namespace OttrOne.StickyPickup
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class StickyPickup : UdonSharpBehaviour
    {
        [Header("StickyPickup v1.1.1")]
        [SerializeField, Tooltip("Root bone for the Sticky Pickup.")]
        private HumanBodyBones Bone;
        [SerializeField, Tooltip("Radius of the spherical tracking area."), Range(0.001f, 4f)]
        private float Radius = 0.3f;
        [SerializeField, Tooltip("Set if Sticky Pickup only works for player in VR.")]
        private bool OnlyVR;
        [SerializeField, Tooltip("Dropping the pickup inside the radius will attach it.")]
        private bool PlaceOnDrop = false;

        [Header("Reset Options")]
        [SerializeField, Tooltip("Trigger collider as a cage for the pickup.")]
        private Collider AreaCollider;
        [SerializeField, Tooltip("Reset height will be ignored if area collider is given.")]
        private float ResetHeight = -50;

        [Header("Debug Options - Disable in productive world")]
        [SerializeField, Tooltip("Expose information to debug console.")]
        private bool DebugMode = false;

        private bool stickedOn = false;

        private Vector3 trackedPos;
        private Quaternion trackedRot;

        private bool isKinematic;

        private Vector3 _origPos;
        private Quaternion _origRot;

        private Rigidbody rigidBody;
        private VRC_Pickup pickup;
        public void Start()
        {
            if (DebugMode) Debug.Log($"[StickyPickup] started for {gameObject.name}");

            this.rigidBody = (Rigidbody)gameObject.GetComponent(typeof(Rigidbody));
            this.pickup = (VRC_Pickup)gameObject.GetComponent(typeof(VRC_Pickup));
            this.isKinematic = rigidBody.isKinematic;

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
            SetKinematic(this.isKinematic);
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

            if (DebugMode) Debug.Log($"[StickyPickup] Set kinematic to {value}");
        }
        public override void OnPickup()
        {
            stickedOn = false;

            SetKinematic(true);
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
                if (Attach()) return;
            }

            // resets kinematic on drop in world to re-enable gravity effect
            SetKinematic(this.isKinematic);

        }

        public override void OnPickupUseUp()
        {
            if (PlaceOnDrop) return;

            Attach();
        }

        private bool Attach()
        {
            Vector3 objPos = this.rigidBody.transform.position;
            Vector3 plyPos = Networking.LocalPlayer.GetBonePosition(Bone);
            Quaternion invRot = Quaternion.Inverse(Networking.LocalPlayer.GetBoneRotation(Bone));

            if (Vector3.Distance(objPos, plyPos) <= Radius && (Networking.LocalPlayer.IsUserInVR() || !OnlyVR))
            {
                stickedOn = true;

                // q^(-1) * Vector (x2-x1, y2-y1, z2-z1)
                trackedPos = invRot * (objPos - plyPos);
                // calculate the rotation by multiplying the current rotation with inverse player rotation
                trackedRot = invRot * this.rigidBody.transform.rotation;
                if (!PlaceOnDrop) pickup.Drop();
                if (DebugMode) Debug.Log($"[StickyPickup] Attached {gameObject.name}");
                return true;
            }

            return false;
        }

        private void OnTriggerExit(Collider collider)
        {
            if (collider == AreaCollider)
            {
                ResetPosition();
            }
        }

        public override void PostLateUpdate()
        {
            if (stickedOn)
            {
                Vector3 bonePosition = Networking.LocalPlayer.GetBonePosition(Bone);
                Quaternion boneRotation = Networking.LocalPlayer.GetBoneRotation(Bone);

                // calculate the wanted offset by multiplying the bonerotation with the position offset and add this to the targeted bone position
                // calculate the wanted rotation by multiplying the current bone rotation with the calculated rotation offset 
                this.rigidBody.transform.SetPositionAndRotation((boneRotation * trackedPos) + bonePosition, boneRotation * trackedRot);
            }
            if (AreaCollider == null && rigidBody.transform.position.y < ResetHeight) ResetPosition();
        }
    }
}