using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Exports the supplied artwork without redrawing or recoloring any lettering.
// The source PNG is preserved byte for byte; only transparent padding is trimmed.
class PrepareWordmark {
    [STAThread] static void Main(string[] args) {
        string source=args[0],root=args[1];byte[] original=File.ReadAllBytes(source);
        BitmapSource image;using(var stream=new MemoryStream(original))image=BitmapFrame.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
        var bgra=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);int width=bgra.PixelWidth,height=bgra.PixelHeight,stride=width*4;
        var pixels=new byte[stride*height];bgra.CopyPixels(pixels,stride,0);int left=width,top=height,right=-1,bottom=-1;
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)if(pixels[y*stride+x*4+3]>0){left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x);bottom=Math.Max(bottom,y);}
        if(right<left)throw new Exception("The supplied artwork is empty.");
        int croppedWidth=right-left+1,croppedHeight=bottom-top+1;
        Directory.CreateDirectory(root);File.WriteAllBytes(Path.Combine(root,"minova-cloud-wordmark-source.png"),original);
        string svg="<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\""+left+" "+top+" "+croppedWidth+" "+croppedHeight+"\" role=\"img\" aria-label=\"Minova Cloud\"><image width=\""+width+"\" height=\""+height+"\" href=\"data:image/png;base64,"+Convert.ToBase64String(original)+"\"/></svg>";
        File.WriteAllText(Path.Combine(root,"minova-cloud-wordmark.svg"),svg);
        int targetWidth=1024,targetHeight=(int)Math.Ceiling(targetWidth*(double)croppedHeight/croppedWidth);
        var crop=new CroppedBitmap(image,new Int32Rect(left,top,croppedWidth,croppedHeight));var visual=new DrawingVisual();using(var drawing=visual.RenderOpen())drawing.DrawImage(crop,new Rect(0,0,targetWidth,targetHeight));
        var bitmap=new RenderTargetBitmap(targetWidth,targetHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using(var output=File.Create(Path.Combine(root,"minova-cloud-wordmark.png")))encoder.Save(output);
        Console.WriteLine("Supplied wordmark bounds: "+left+","+top+" "+croppedWidth+"x"+croppedHeight+"; UI export: "+targetWidth+"x"+targetHeight);
    }
}
