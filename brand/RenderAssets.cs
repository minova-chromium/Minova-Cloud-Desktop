using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using System.Linq;

// Deterministic raster exports of the editable Cloud/M vector master.
class RenderAssets {
    const string Mark="M120 430 C55 430 12 386 12 330 C12 276 48 232 100 220 C99 213 98 202 98 194 C98 112 159 48 240 48 C306 48 362 92 378 154 C448 156 500 211 500 280 C500 366 442 430 360 430 Z";
    static XDocument original;
    static Color ColorOf(string value){return (Color)ColorConverter.ConvertFromString(value);}
    static void Cloud(DrawingContext drawing,double x,double y,double size) {
        var brush=new LinearGradientBrush();brush.StartPoint=new Point(0,0);brush.EndPoint=new Point(1,1);brush.GradientStops.Add(new GradientStop(ColorOf("#1A3348"),0));brush.GradientStops.Add(new GradientStop(ColorOf("#101E2D"),.55));brush.GradientStops.Add(new GradientStop(ColorOf("#080D14"),1));
        drawing.PushTransform(new TranslateTransform(x,y));drawing.PushTransform(new ScaleTransform(size/512,size/512));
        drawing.DrawGeometry(brush,new Pen(new LinearGradientBrush(ColorOf("#4199B5"),ColorOf("#1A3545"),45),3),Geometry.Parse(Mark));
        drawing.PushTransform(new TranslateTransform(56.32,72.32));drawing.PushTransform(new ScaleTransform(.78,.78));
        XNamespace ns="http://www.w3.org/2000/svg";
        foreach(var node in original.Root.Element(ns+"g").Elements(ns+"path")){
            string fill=(string)node.Attribute("fill");Brush color;
            if(fill.StartsWith("url(#")){
                string id=fill.Substring(5,fill.Length-6);var gradient=original.Descendants(ns+"linearGradient").Single(g=>(string)g.Attribute("id")==id);
                var linear=new LinearGradientBrush {MappingMode=BrushMappingMode.Absolute,StartPoint=new Point((double)gradient.Attribute("x1"),(double)gradient.Attribute("y1")),EndPoint=new Point((double)gradient.Attribute("x2"),(double)gradient.Attribute("y2"))};
                foreach(var stop in gradient.Elements(ns+"stop")){var c=ColorOf((string)stop.Attribute("stop-color"));c.A=(byte)(255*((double?)stop.Attribute("stop-opacity")??1));linear.GradientStops.Add(new GradientStop(c,(double?)stop.Attribute("offset")??0));}color=linear;
            }else color=new SolidColorBrush(ColorOf(fill));
            drawing.PushOpacity((double?)node.Attribute("opacity")??1);drawing.DrawGeometry(color,null,Geometry.Parse((string)node.Attribute("d")));drawing.Pop();
        }
        drawing.Pop();drawing.Pop();drawing.Pop();drawing.Pop();
    }
    static byte[] Icon(int size,bool background) {
        var visual=new DrawingVisual();using(var drawing=visual.RenderOpen()){if(background)drawing.DrawRoundedRectangle(new LinearGradientBrush(ColorOf("#142637"),ColorOf("#080C12"),45),null,new Rect(0,0,size,size),size*112d/512,size*112d/512);Cloud(drawing,0,0,size);}
        return Png(visual,size,size);
    }
    static byte[] Png(DrawingVisual visual,int width,int height){var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=new MemoryStream()){encoder.Save(stream);return stream.ToArray();}}
    [STAThread] static void Main(string[] args) {
        string root=args[0];original=XDocument.Load(Path.Combine(root,"original-minova-m.svg"));Directory.CreateDirectory(root);File.WriteAllBytes(Path.Combine(root,"minova-cloud-icon.png"),Icon(1024,true));File.WriteAllBytes(Path.Combine(root,"minova-cloud-symbol.png"),Icon(512,false));
        int[] sizes={16,24,32,48,64,128,256};var images=Array.ConvertAll(sizes,size=>Icon(size,true));using(var file=File.Create(Path.Combine(root,"minova-cloud.ico")))using(var writer=new BinaryWriter(file)){writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)sizes.Length);int offset=6+16*sizes.Length;for(int i=0;i<sizes.Length;i++){writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);writer.Write(images[i].Length);writer.Write(offset);offset+=images[i].Length;}foreach(var image in images)writer.Write(image);}
        var lockup=new DrawingVisual();using(var drawing=lockup.RenderOpen()){Cloud(drawing,0,0,160);var text=new FormattedText("MINOVA",System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(new FontFamily("Segoe UI"),FontStyles.Normal,FontWeights.SemiBold,FontStretches.Normal),58,Brushes.White,1);drawing.DrawText(text,new Point(182,39));}File.WriteAllBytes(Path.Combine(root,"minova-cloud-lockup.png"),Png(lockup,464,160));
        var preview=new DrawingVisual();using(var drawing=preview.RenderOpen()){drawing.DrawRectangle(new SolidColorBrush(ColorOf("#080C12")),null,new Rect(0,0,1000,640));Cloud(drawing,308,48,384);var text=new FormattedText("MINOVA CLOUD",System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(new FontFamily("Segoe UI"),FontStyles.Normal,FontWeights.SemiBold,FontStretches.Normal),48,Brushes.White,1);drawing.DrawText(text,new Point((1000-text.Width)/2,468));}File.WriteAllBytes(Path.Combine(root,"minova-cloud-logo-preview.png"),Png(preview,1000,640));
    }
}
