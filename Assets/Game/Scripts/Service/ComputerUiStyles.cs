using System;
using UnityEngine;
namespace MotelGame.Service
{
    // Shared only by the existing IMGUI workstation views.
    internal static class ComputerUiStyles
    {
        static GUIStyle button,tooltip;
        public static GUIStyle Button {get{Ensure();return button;}}
        public static GUIStyle Tooltip {get{Ensure();return tooltip;}}
        static Texture2D Fill(Color fill)
        {
            var t=new Texture2D(3,3){hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Point};
            for(int x=0;x<3;x++)for(int y=0;y<3;y++)t.SetPixel(x,y,x==1&&y==1?fill:fill*.7f);
            t.Apply();return t;
        }
        static void Ensure()
        {
            if(button!=null)return;
            var normal=Fill(new Color(.70f,.73f,.66f));var hover=Fill(new Color(.84f,.86f,.78f));var active=Fill(new Color(.55f,.66f,.57f));
            button=new GUIStyle(GUI.skin.button){fontSize=14,fontStyle=FontStyle.Normal,fixedHeight=30,wordWrap=true,
                padding=new RectOffset(8,8,4,4),border=new RectOffset(1,1,1,1),margin=new RectOffset(4,4,3,3)};
            foreach(var s in new[]{button.normal,button.hover,button.active,button.focused,button.onNormal,button.onHover,button.onActive,button.onFocused})
            {
                s.textColor=new Color(.07f,.10f,.09f);s.background=normal;
#if UNITY_EDITOR
                s.scaledBackgrounds=Array.Empty<Texture2D>();
#endif
            }
            button.hover.background=button.focused.background=button.onHover.background=hover;
            button.active.background=button.onNormal.background=button.onActive.background=button.onFocused.background=active;
            tooltip=new GUIStyle(GUI.skin.label){fontSize=13,padding=new RectOffset(9,9,6,6),border=new RectOffset(1,1,1,1)};
            tooltip.normal.background=Fill(new Color(.08f,.12f,.11f));tooltip.normal.textColor=Color.white;
#if UNITY_EDITOR
            tooltip.normal.scaledBackgrounds=Array.Empty<Texture2D>();
#endif
        }
    }
}
