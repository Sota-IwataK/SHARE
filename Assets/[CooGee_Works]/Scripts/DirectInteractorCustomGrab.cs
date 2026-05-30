// using System.Collections;
// using System.Collections.Generic;
// using UnityEngine;


// public class DirectInteractorCustomGrab : MonoBehaviour
// {
//     private UnityEngine.XR.Interaction.Toolkit.Interactors.XRDirectInteractor _directInteractor;

//     private void Start()
//     {
//         _directInteractor = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRDirectInteractor>();
//     }

//     public void Grab()
//     {
//         if (!_directInteractor.allowSelect)
//         {
//             return;
//         }

//         if (_directInteractor.hasSelection)
//         {
//             return;
//         }

//         if (_directInteractor.hasHover)
//         {
//             _directInteractor.StartManualInteraction((UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable)_directInteractor.interactablesHovered[0]);
//         }
//     }

//     public void Release()
//     {
//         if (_directInteractor.isPerformingManualInteraction)
//         {
//             _directInteractor.EndManualInteraction();
//         }
//     }
// }
