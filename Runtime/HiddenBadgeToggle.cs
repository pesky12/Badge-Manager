using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;


namespace PeskyBox.BadgeManager
{
    public enum BadgeToggleType
    {
        ShowToggleableBadges,
        ShowMyBadge,
        ShowAllBadges
    }
    
    [RequireComponent(typeof(Toggle))]
    public class HiddenBadgeToggle : MonoBehaviour, IEditorOnly
    {
        [Header("Hidden Badge Toggle")]
        [Header("This component is for editor use only")]
        public BadgeToggleType badgeToggleType;
    }
}