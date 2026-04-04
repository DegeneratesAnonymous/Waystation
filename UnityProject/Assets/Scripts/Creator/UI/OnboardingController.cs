using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Waystation.Creator.UI
{
    public class OnboardingStep
    {
        public string targetElement;
        public string title;
        public string description;
        public string position; // "top" | "bottom" | "left" | "right"
    }

    public class OnboardingController
    {
        private readonly VisualElement _root;
        private int _currentStep;
        private List<OnboardingStep> _steps;
        private VisualElement _tooltipElement;

        public bool IsActive { get; private set; }

        public OnboardingController(VisualElement root)
        {
            _root = root;
        }

        public void StartOnboarding(string assetType)
        {
            _steps = GetStepsForType(assetType);
            _currentStep = 0;
            IsActive = true;
            ShowStep();
        }

        public void NextStep()
        {
            _currentStep++;
            if (_currentStep >= _steps.Count)
            {
                EndOnboarding();
                return;
            }
            ShowStep();
        }

        public void SkipAll()
        {
            EndOnboarding();
            CreatorSettings.TipsDismissed = true;
        }

        private void EndOnboarding()
        {
            IsActive = false;
            _tooltipElement?.RemoveFromHierarchy();
            _tooltipElement = null;
        }

        private void ShowStep()
        {
            _tooltipElement?.RemoveFromHierarchy();

            if (_currentStep >= _steps.Count) return;
            var step = _steps[_currentStep];

            _tooltipElement = new VisualElement();
            _tooltipElement.AddToClassList("onboarding-tooltip");

            var title = new Label(step.title);
            title.AddToClassList("onboarding-title");

            var desc = new Label(step.description);
            desc.AddToClassList("onboarding-desc");

            var btnRow = new VisualElement();
            btnRow.AddToClassList("onboarding-btn-row");

            var nextBtn = new Button(NextStep);
            nextBtn.text = _currentStep < _steps.Count - 1 ? "Next" : "Done";
            nextBtn.AddToClassList("onboarding-btn");

            var skipBtn = new Button(SkipAll);
            skipBtn.text = "Skip";
            skipBtn.AddToClassList("onboarding-skip-btn");

            btnRow.Add(skipBtn);
            btnRow.Add(nextBtn);

            _tooltipElement.Add(title);
            _tooltipElement.Add(desc);
            _tooltipElement.Add(btnRow);

            var stepCounter = new Label($"Step {_currentStep + 1} of {_steps.Count}");
            stepCounter.AddToClassList("onboarding-counter");
            _tooltipElement.Add(stepCounter);

            _root.Add(_tooltipElement);

            // Position near target element
            var target = _root.Q(step.targetElement);
            if (target != null)
            {
                _tooltipElement.style.position = Position.Absolute;
                var bounds = target.worldBound;
                switch (step.position)
                {
                    case "bottom":
                        _tooltipElement.style.left = bounds.x;
                        _tooltipElement.style.top = bounds.yMax + 8;
                        break;
                    case "top":
                        _tooltipElement.style.left = bounds.x;
                        _tooltipElement.style.top = bounds.y - 120;
                        break;
                    case "right":
                        _tooltipElement.style.left = bounds.xMax + 8;
                        _tooltipElement.style.top = bounds.y;
                        break;
                    case "left":
                        _tooltipElement.style.left = bounds.x - 260;
                        _tooltipElement.style.top = bounds.y;
                        break;
                }
            }
        }

        private List<OnboardingStep> GetStepsForType(string type)
        {
            var steps = new List<OnboardingStep>
            {
                new OnboardingStep
                {
                    targetElement = "toolbar",
                    title = "Welcome to Creator Mode!",
                    description = "This is your pixel art workspace. Let's take a quick tour.",
                    position = "bottom"
                },
                new OnboardingStep
                {
                    targetElement = "canvas-image",
                    title = "The Canvas",
                    description = "Draw your tile art here. Use the scroll wheel to zoom and right-click to pan.",
                    position = "right"
                },
                new OnboardingStep
                {
                    targetElement = "btn-pencil",
                    title = "Drawing Tools",
                    description = "Select tools from the toolbar. Press P for Pencil, E for Eraser, G for Fill.",
                    position = "right"
                },
                new OnboardingStep
                {
                    targetElement = "swatch-grid",
                    title = "Colour Palette",
                    description = "Choose from the shared palette colours. These are designed to match the game's art style.",
                    position = "left"
                },
                new OnboardingStep
                {
                    targetElement = "context-preview",
                    title = "Preview",
                    description = "See how your tile looks in a 3×3 grid context as you paint.",
                    position = "left"
                },
                new OnboardingStep
                {
                    targetElement = "btn-export",
                    title = "Export",
                    description = "When you're done, export your tile as an atlas+sidecar for use in the game.",
                    position = "bottom"
                }
            };

            if (type == "wall_tile")
            {
                steps.Insert(4, new OnboardingStep
                {
                    targetElement = "bitmask-nav",
                    title = "Wall Bitmask Variants",
                    description = "Walls need 16 directional variants. Draw key variants (None, NS, EW) then auto-generate the rest.",
                    position = "left"
                });
            }

            if (type == "furniture")
            {
                steps.Insert(4, new OnboardingStep
                {
                    targetElement = "variant-tabs",
                    title = "Direction Variants",
                    description = "Furniture can face South, North, and Side directions. Draw each one separately.",
                    position = "left"
                });
                steps.Insert(5, new OnboardingStep
                {
                    targetElement = "properties-section",
                    title = "Footprint & Properties",
                    description = "Set the footprint size (up to 4×4), interaction point, and other properties.",
                    position = "left"
                });
            }

            return steps;
        }
    }
}
