namespace DellGamingControlCenter.App.Controls;

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;

/// <summary>
/// Interaction logic for Hardware3DCoreView.xaml.
/// Real 3D rendering component using native WPF Viewport3D, frozen MeshGeometry3D,
/// and hardware-accelerated DirectX lighting/materials.
/// Zero background CPU/GPU thread spin when idle.
/// </summary>
public partial class Hardware3DCoreView : UserControl
{
    private static readonly Point3D DefaultCameraPosition = new(2.8, 2.5, 3.8);
    private static readonly Vector3D DefaultLookDirection = new(-2.8, -2.5, -3.8);

    public Hardware3DCoreView()
    {
        InitializeComponent();
        Build3DScene();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RootContainer.MouseMove += OnContainerMouseMove;
        RootContainer.MouseLeave += OnContainerMouseLeave;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        RootContainer.MouseMove -= OnContainerMouseMove;
        RootContainer.MouseLeave -= OnContainerMouseLeave;
    }

    private void Build3DScene()
    {
        var modelGroup = new Model3DGroup();

        // 1. Lighting Setup (Neutral industrial studio lighting with restrained cyan rim)
        // Ambient base light for soft global illumination
        var ambientLight = new AmbientLight(Color.FromRgb(40, 46, 52));
        modelGroup.Children.Add(ambientLight);

        // Key directional light (crisp cool white)
        var keyLight = new DirectionalLight(
            Color.FromRgb(240, 245, 250),
            new Vector3D(-1.5, -2.5, -2.0));
        modelGroup.Children.Add(keyLight);

        // Fill directional light (neutral slate)
        var fillLight = new DirectionalLight(
            Color.FromRgb(70, 80, 90),
            new Vector3D(2.0, 1.0, 1.5));
        modelGroup.Children.Add(fillLight);

        // Precision cyber rim light (controlled signal cyan edge highlight)
        var rimLight = new DirectionalLight(
            Color.FromRgb(66, 217, 245),
            new Vector3D(1.0, -1.0, -1.0));
        modelGroup.Children.Add(rimLight);

        // 2. Chassis Baseboard (Machined Graphite Plinth)
        var baseBuilder = new MeshBuilder3D();
        baseBuilder.AddBox(new Point3D(0, -0.15, 0), 2.5, 0.12, 2.1);
        var baseMesh = baseBuilder.ToMesh();

        var baseMatGroup = new MaterialGroup();
        baseMatGroup.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(17, 19, 21))));
        baseMatGroup.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(35, 41, 48)), 15));
        baseMatGroup.Freeze();

        modelGroup.Children.Add(new GeometryModel3D(baseMesh, baseMatGroup));

        // 3. Processor IHS / Heat Spreader (Brushed Titanium Block)
        var ihsBuilder = new MeshBuilder3D();
        ihsBuilder.AddBox(new Point3D(0, 0.02, 0), 1.1, 0.10, 1.1);
        var ihsMesh = ihsBuilder.ToMesh();

        var ihsMatGroup = new MaterialGroup();
        ihsMatGroup.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(34, 39, 43))));
        ihsMatGroup.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(166, 175, 183)), 35));
        ihsMatGroup.Freeze();

        modelGroup.Children.Add(new GeometryModel3D(ihsMesh, ihsMatGroup));

        // 4. Silicon Core Die (Signal Cyan Reflective Element)
        var dieBuilder = new MeshBuilder3D();
        dieBuilder.AddBox(new Point3D(0, 0.11, 0), 0.55, 0.04, 0.55);
        var dieMesh = dieBuilder.ToMesh();

        var dieMatGroup = new MaterialGroup();
        dieMatGroup.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(66, 217, 245))));
        dieMatGroup.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(200, 245, 255)), 80));
        dieMatGroup.Freeze();

        modelGroup.Children.Add(new GeometryModel3D(dieMesh, dieMatGroup));

        // 5. Dual Cooling Fin Stacks (Dark Aluminum Dissipation Fins)
        var finsBuilder = new MeshBuilder3D();
        // Left Fin Array
        for (int i = 0; i < 5; i++)
        {
            double x = -0.95 + (i * 0.07);
            finsBuilder.AddBox(new Point3D(x, 0.15, 0), 0.035, 0.35, 1.5);
        }
        // Right Fin Array
        for (int i = 0; i < 5; i++)
        {
            double x = 0.67 + (i * 0.07);
            finsBuilder.AddBox(new Point3D(x, 0.15, 0), 0.035, 0.35, 1.5);
        }
        var finsMesh = finsBuilder.ToMesh();

        var finsMatGroup = new MaterialGroup();
        finsMatGroup.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(26, 29, 32))));
        finsMatGroup.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(80, 90, 100)), 25));
        finsMatGroup.Freeze();

        modelGroup.Children.Add(new GeometryModel3D(finsMesh, finsMatGroup));

        // 6. Thermal Heatpipe Standoffs (Precision Copper Alloy)
        var pipesBuilder = new MeshBuilder3D();
        pipesBuilder.AddCylinder(new Point3D(-0.45, -0.05, -0.45), 0.04, 0.28, 12);
        pipesBuilder.AddCylinder(new Point3D(0.45, -0.05, -0.45), 0.04, 0.28, 12);
        pipesBuilder.AddCylinder(new Point3D(-0.45, -0.05, 0.45), 0.04, 0.28, 12);
        pipesBuilder.AddCylinder(new Point3D(0.45, -0.05, 0.45), 0.04, 0.28, 12);
        var pipesMesh = pipesBuilder.ToMesh();

        var pipesMatGroup = new MaterialGroup();
        pipesMatGroup.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(180, 110, 60)))); // Copper alloy
        pipesMatGroup.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(220, 150, 90)), 40));
        pipesMatGroup.Freeze();

        modelGroup.Children.Add(new GeometryModel3D(pipesMesh, pipesMatGroup));

        // Assign to Viewport3D
        modelGroup.Freeze();
        RootModelGroupVisual.Content = modelGroup;
    }

    private void OnContainerMouseMove(object sender, MouseEventArgs e)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        Point pos = e.GetPosition(RootContainer);
        double normX = (pos.X / ActualWidth) * 2.0 - 1.0;   // -1 to +1
        double normY = (pos.Y / ActualHeight) * 2.0 - 1.0;  // -1 to +1

        // Restrained subtle parallax: max offset 0.45 units
        double offsetX = normX * 0.45;
        double offsetY = -normY * 0.35;

        Point3D targetPos = new(
            DefaultCameraPosition.X + offsetX,
            DefaultCameraPosition.Y + offsetY,
            DefaultCameraPosition.Z);

        CoreCamera.Position = targetPos;
        CoreCamera.LookDirection = new Vector3D(
            -targetPos.X,
            -targetPos.Y,
            -targetPos.Z);

        int degX = (int)(normX * 18);
        ParallaxStatusText.Text = $"ACTIVE INSPECTION ({degX:+0;-0;0}°)";
    }

    private void OnContainerMouseLeave(object sender, MouseEventArgs e)
    {
        ResetToDefaultView();
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        ResetToDefaultView();
    }

    private void ResetToDefaultView()
    {
        // Smooth return animation or clean snap
        var posAnim = new Point3DAnimation(
            DefaultCameraPosition,
            TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        var dirAnim = new Vector3DAnimation(
            DefaultLookDirection,
            TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        CoreCamera.BeginAnimation(PerspectiveCamera.PositionProperty, posAnim);
        CoreCamera.BeginAnimation(PerspectiveCamera.LookDirectionProperty, dirAnim);

        ParallaxStatusText.Text = "HOVER TO INSPECT";
    }
}
