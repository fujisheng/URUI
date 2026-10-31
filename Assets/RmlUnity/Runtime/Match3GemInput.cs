using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace RmlUnity {
 // A separate MonoBehaviour script asset so Unity serializes a real m_Script
 // reference in the saved uGUI Prefab for every gem.
 public sealed class Match3GemInput : MonoBehaviour,IPointerDownHandler,IPointerUpHandler,ICancelHandler {
  public int index;
  [NonSerialized] public Match3UGUIView owner;
  public void OnPointerDown(PointerEventData eventData){owner?.OnGemDown(index,eventData.pointerId);}
  public void OnPointerUp(PointerEventData eventData){owner?.OnGemUp(index,eventData);}
  public void OnCancel(BaseEventData eventData){owner?.OnGemCancel();}
 }
}
