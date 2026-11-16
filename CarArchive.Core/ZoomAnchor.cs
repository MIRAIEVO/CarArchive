namespace CarArchive.Core;
public static class ZoomAnchor
{
 public static (double X,double Y,float Zoom) At(double x,double y,float oldZoom,float newZoom,double mouseX,double mouseY,double contentWidth,double contentHeight,double viewportWidth,double viewportHeight)
 {var zoom=Math.Clamp(newZoom,1,8);var ratio=zoom/Math.Max(.001,oldZoom);return(Math.Clamp((x+mouseX)*ratio-mouseX,0,Math.Max(0,contentWidth*zoom-viewportWidth)),Math.Clamp((y+mouseY)*ratio-mouseY,0,Math.Max(0,contentHeight*zoom-viewportHeight)),zoom);}
}
