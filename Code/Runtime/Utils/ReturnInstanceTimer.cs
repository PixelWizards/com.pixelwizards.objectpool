using UnityEngine;

namespace PixelWizards.ObjectPool
{
    /// <summary>
    /// Automatically return an object to the Pool when the timer expires
    /// </summary>
    public class ReturnInstanceTimer : MonoBehaviour
    {
        /// <summary>
        /// Set the timer for how long before we return this object to the pool
        /// </summary>
        public float timer = 2.0f;
        
        // Internal
        private float origTimer = 3.0f;

        /************************
         * Public API
         ************************/

        /// <summary>
        /// Set the timer. 
        /// </summary>
        /// <param name="thisTime">How long the timer it?</param>
        public void SetTimer(float thisTime)
        {
            origTimer = thisTime;
            timer = origTimer;
        }
        
        /************************
         * Private / Internals
         ************************/
        
        /// <summary>
        /// Reset the timer OnEnable
        /// </summary>
        private void OnEnable()
        {
            timer = origTimer;
        }

        /// <summary>
        /// start the countdown and reset 
        /// </summary>
        private void Update()
        {
            timer -= Time.deltaTime;

            if (timer < 0f)
            {
                timer = origTimer;
                PoolManager.ReturnInstance(gameObject);
            }
        }
    }
}