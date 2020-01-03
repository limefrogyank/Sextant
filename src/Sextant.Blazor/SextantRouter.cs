﻿// Copyright (c) 2019 .NET Foundation and Contributors. All rights reserved.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;
using ReactiveUI;
using Splat;

namespace Sextant.Blazor
{
    /// <summary>
    /// The router specifically designed for use by Sextant.
    /// </summary>
#pragma warning disable CA1063 // Implement IDisposable Correctly
    public class SextantRouter : ComponentBase, IView, IDisposable
#pragma warning restore CA1063 // Implement IDisposable Correctly
    {
        private UrlParameterViewModelGenerator _urlParameterVMGenerator;
        private IScheduler _mainScheduler;
        private RouteViewViewModelLocator _routeLocator;
        private bool _initialized;
        private bool _firstPageRendered;
        private IViewStackService _stackService;
        private IModalView _modalReference;

        private Dictionary<string, IViewModel> _viewModelDictionary = new Dictionary<string, IViewModel>();

        // Need to mirror the viewmodel stack on IViewStackService.
        private Stack<IViewModel> _mainStack = new Stack<IViewModel>();

        // This will NEVER be accessed by the programmer.  Instead, it only gets filled when hitting the browser back button.
        private Stack<IViewModel> _forwardStack = new Stack<IViewModel>();

        // Mirrored modal stack on IViewStackService.
        private Stack<Type> _modalBackStack = new Stack<Type>();

        /// <summary>
        /// Gets or sets the content to display when a match is found for the requested route.
        /// </summary>
        [Parameter]
        public RenderFragment<RouteData> Found { get; set; }

        /// <summary>
        /// Gets or sets where the not found content.
        /// </summary>
        [Parameter]
        public RenderFragment NotFound { get; set; }

        /// <summary>
        /// Gets or sets where the views are located.
        /// </summary>
        [Parameter]
        public Assembly AppAssembly { get; set; }

        /// <summary>
        /// Gets or sets the component that displays the modal.
        /// </summary>
        [Parameter]
        public Type ModalComponent { get; set; }

        /// <inheritdoc/>
        public IScheduler MainThreadScheduler => _mainScheduler;

        /// <inheritdoc/>
        public IObservable<IViewModel> PagePopped { get; set; }

        internal IViewModel CurrentViewModel => _mainStack.Count > 0 ? _mainStack.Peek() : null;

        internal object CurrentView { get; set; }

        [Inject]
        private IJSRuntime JSRuntime { get; set; }

        [Inject]
        private NavigationManager BlazorNavigationManager { get; set; }

        /// <inheritdoc/>
        public IObservable<Unit> PopModal()
        {
            return Observable.Start(
                async () =>
                {
                    if (_modalReference == null)
                    {
                        throw new Exception("Your modal component type hasn't been defined in SextantRouter.  Make sure it implements IModalView.");
                    }

                    var modalNavigatingAwayFrom = _modalBackStack.Pop();

                    var modalStack = await _stackService.ModalStack.FirstOrDefaultAsync();
                    if (modalStack.Count > 1 && _modalBackStack.Count > 0)
                    {
                        var previousViewModel = modalStack[modalStack.Count - 2];
                        var viewType = _modalBackStack.Peek();

                        await _modalReference.ShowViewAsync(viewType, previousViewModel).ConfigureAwait(false);
                    }
                    else
                    {
                        await _modalReference.HideAsync().ConfigureAwait(false);
                    }

                    return Unit.Default;
                }).Concat();
        }

        /// <inheritdoc/>
        public IObservable<Unit> PopPage(bool animate = true)
        {
            return Observable.Start(
                async () =>
                {
                    await SextantNavigationManager.Instance.GoBackAsync().ConfigureAwait(false);
                    return Unit.Default;
                }).Concat();
        }

        /// <inheritdoc/>
        public IObservable<Unit> PopToRootPage(bool animate = true)
        {
            // Since a popstate event is coming, we're relying on the popped observable to do any stack manipulation.
            // PopRootAndTick should never be called on the ViewStackServiceBase... hence the `IgnoreElements()` call at the end.
            return Observable.Start(
                async () =>
                {
                    var count = _mainStack.Count;

                    await SextantNavigationManager.Instance.GoToRootAsync((count - 1) * -1).ConfigureAwait(false);

                    return Unit.Default;
                })
                .Concat()
                .IgnoreElements();
        }

        /// <inheritdoc/>
        public IObservable<Unit> PushModal(IViewModel modalViewModel, string contract, bool withNavigationPage = true)
        {
            return Observable.Start(
                async () =>
                {
                    if (_modalReference == null)
                    {
                        throw new Exception("Your modal component type hasn't been defined in SextantRouter.  Make sure it implements IModalView.");
                    }

                    var viewType = _routeLocator.ResolveViewType(modalViewModel.GetType(), string.IsNullOrWhiteSpace(contract) ? null : contract);

                    if (viewType == null)
                    {
                        throw new Exception($"A view hasn't been registered for the viewmodel type, {modalViewModel.GetType()}, with contract, {contract}.");
                    }

                    // Save the type in a view backstack for later.  Since the StackService doesn't save the contract, we won't know exactly what view to use otherwise.
                    _modalBackStack.Push(viewType);

                    await _modalReference.ShowViewAsync(viewType, modalViewModel).ConfigureAwait(false);

                    return Unit.Default;
                })
                .Concat();
        }

        /// <inheritdoc/>
        public IObservable<Unit> PushPage(IViewModel viewModel, string contract, bool resetStack, bool animate = true)
        {
            return Observable.Start(
                () =>
                {
                    _mainStack.Push(viewModel);
                    var route = _routeLocator.ResolveRoute(viewModel.GetType());
                    while (route.StartsWith("/", StringComparison.InvariantCulture))
                    {
                        route = route.Remove(0, 1);
                    }

                    // Check if this is a browser-forward nav... if so, pop the most recent viewmodel.  If not, clear forward stack and proceed normally to navigate internally.
                    if (_forwardStack.Count > 0 && _forwardStack.Peek() == viewModel)
                    {
                        _forwardStack.Pop();
                    }
                    else
                    {
                        while (_forwardStack.Count > 0)
                        {
                            var vm = _forwardStack.Pop();
                            var pairToDelete = _viewModelDictionary.FirstOrDefault(x => x.Value == vm);
                            if (!pairToDelete.Equals(default(KeyValuePair<string, IViewModel>)))
                            {
                                _viewModelDictionary.Remove(pairToDelete.Key);
                            }
                        }

                        // If this is the first page load, then the internal router has already taken care of navigation. Skip it.
                        if (_firstPageRendered)
                        {
                            BlazorNavigationManager.NavigateTo(SextantNavigationManager.Instance.BaseUri + route);

                            // UriHelper.NavigateTo(SextantNavigationManager.Instance.BaseUri + route);
                        }
                    }

                    var pair = _viewModelDictionary.FirstOrDefault(x => x.Value == viewModel);
                    if (pair.Equals(default(KeyValuePair<string, IViewModel>)))
                    {
                        pair = new KeyValuePair<string, IViewModel>(Guid.NewGuid().ToString(), viewModel);
                        _viewModelDictionary.Add(pair.Key, pair.Value);
                    }

                    _ = SextantNavigationManager.Instance.ReplaceStateAsync(pair.Key);
                    return Unit.Default;
                },
                RxApp.MainThreadScheduler);
        }

        /// <inheritdoc/>
        protected override Task OnInitializedAsync()
        {
            _mainScheduler = RxApp.MainThreadScheduler;
            _routeLocator = (RouteViewViewModelLocator)Splat.Locator.Current.GetService(typeof(RouteViewViewModelLocator));
            _urlParameterVMGenerator = (UrlParameterViewModelGenerator)Splat.Locator.Current.GetService(typeof(UrlParameterViewModelGenerator));

            PagePopped = Observable.FromEventPattern<NavigationActionEventArgs>(
                   x => SextantNavigationManager.Instance.LocationChanged += x,
                   x => SextantNavigationManager.Instance.LocationChanged -= x)
               .Where(ep => ep.EventArgs.NavigationType == SextantNavigationType.Popstate)
               .ObserveOn(RxApp.MainThreadScheduler)
               .Select(async (ep) =>
               {
                   List<IViewModel> viewModels = new List<IViewModel>();
                   string id = null;
                   bool found = false;

                   // If this is false, we're probably pre-rendering.
                   if (_viewModelDictionary.ContainsKey(ep.EventArgs.Id))
                   {
                       // This might not be a simple back navigation.  Could be any page in the history.  Need to find this target vm in the stack(s).
                       IViewModel targetViewModel = _viewModelDictionary[ep.EventArgs.Id];

                       Debug.WriteLine($"ViewModel that comes next: {targetViewModel.GetType().Name}");

                       // Assumes pop event is back navigation.
                       foreach (var vm in _mainStack)
                       {
                           if (vm != targetViewModel)
                           {
                               viewModels.Add(vm);
                           }
                           else
                           {
                               found = true;
                               break;
                           }
                       }

                       if (found)
                       {
                           // Stick them in the forward stack now that we know it was a browser back navigation.
                           foreach (var vm in viewModels)
                           {
                               _forwardStack.Push(vm);
                               _mainStack.Pop();
                           }

                           return viewModels;
                       }

                       // If we get here, there was **definitely** forward navigation instead!
                       viewModels.Clear();
                       do
                       {
                           var vm = _forwardStack.Peek();
                           if (vm == targetViewModel)
                           {
                               found = true;
                           }

                           await _stackService.PushPage(_forwardStack.Peek(), null, false);

                           // We're keeping the active viewmodel on the forwardstack so that when we call pushpage, we can recognize it was a forward button nav
                           // and not send the nav command to the internal router.
                       }
                       while (!found && _forwardStack.Count > 0);
                   }

                   return viewModels;
               })
               .Concat()
               .SelectMany(x => x);

            Locator.CurrentMutable.RegisterStackServices(this);
            _stackService = Locator.Current.GetService<IViewStackService>();

            return base.OnInitializedAsync();
        }

        /// <inheritdoc/>
        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!_initialized)
            {
                _initialized = true;
#pragma warning disable RCS1090 // Call 'ConfigureAwait(false)'.
                await SextantNavigationManager.Instance.InitializeAsync(JSRuntime);
                var result = ParseRelativeUrl(SextantNavigationManager.Instance.AbsoluteUri);

                await _stackService.PushPage(result.viewModel, result.contract, true, false); // .Subscribe();
#pragma warning restore RCS1090 // Call 'ConfigureAwait(false)'

                // Need this for first page load
                ((IViewFor)CurrentView).ViewModel = CurrentViewModel;
                _firstPageRendered = true;

                // SextantNavigationManager.Instance.ReplaceStateAsync(CurrentViewModel.GetHashCode().ToString());
            }
        }

        /// <inheritdoc/>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1118:Parameter should not span multiple lines", Justification = "Looks good.")]
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingValue<SextantRouter>>(0);
            builder.AddAttribute(1, nameof(CascadingValue<SextantRouter>.Value), this);
            builder.AddAttribute(2, nameof(CascadingValue<SextantRouter>.ChildContent), (RenderFragment)((builder2) =>
            {
                builder2.OpenComponent<Router>(3);
                builder2.AddAttribute(4, nameof(Router.AppAssembly), AppAssembly);
                builder2.AddAttribute(5, nameof(Router.Found), Found);
                builder2.AddAttribute(6, nameof(Router.NotFound), NotFound);
                builder2.CloseComponent();
            }));
            builder.CloseComponent();

            if (ModalComponent != null)
            {
                builder.OpenComponent(7, ModalComponent);
                builder.AddComponentReferenceCapture(8, (componentReference) =>
                {
                    if (!(componentReference is IModalView))
                    {
                        throw new Exception("Your modal needs to implement IModalView so SextantRouter knows how to use it.");
                    }

                    _modalReference = (IModalView)componentReference;
                });
                builder.CloseComponent();
            }
        }

        private async void Instance_LocationChanged(object sender, NavigationActionEventArgs e)
        {
            _ = sender;
            System.Diagnostics.Debug.WriteLine($"Location changed: {e.Uri}  {e.Id}");

            // back or forward event will set history automatically... but link click will not.
            if (e.NavigationType == SextantNavigationType.Url)
            {
                // link click
                var results = ParseRelativeUrl(e.Uri, e.Id);

                await _stackService.PushPage(results.viewModel, results.contract, false, true);
            }
            else
            {
                // popstate
            }
        }

        private (IViewModel viewModel, string contract) ParseRelativeUrl(string url, string id = null)
        {
            IViewModel viewModel = null;
            Type viewModelType = null;
            char[] separator = new[] { '?' };
            var relativeUri = SextantNavigationManager.Instance.ToBaseRelativePath(url);

            var segments = relativeUri.Split(separator, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < segments.Length; i++)
            {
                segments[i] = Uri.UnescapeDataString(segments[i]);
            }

            // For future, could add interface called IBlazorState where viewmodels are initialized with parameters.
            var parameters = new Dictionary<string, string>();
            if (segments.Length == 2)
            {
                var queryParameters = segments[1].Split(new[] { '&' });
                foreach (var p in queryParameters)
                {
                    var psplit = p.Split(new[] { '=' });
                    parameters.Add(Uri.UnescapeDataString(psplit[0]), Uri.UnescapeDataString(psplit[1]));
                }
            }

            if (segments.Length == 0)
            {
                viewModelType = _routeLocator.ResolveViewModelType("/");
            }
            else
            {
                viewModelType = _routeLocator.ResolveViewModelType("/" + segments[0]);
            }

            if (id == null)
            {
                viewModel = _urlParameterVMGenerator.GetViewModel(viewModelType, parameters);
            }
            else
            {
                // skip modals... shouldn't navigate modals using URL bar
                // var modalStack = await _viewStackService.ModalStack.FirstAsync();
                // var foundViewModel = modalStack.FirstOrDefault(x => x.Id == id);
                // var topModalVM = await _viewStackService.TopPage();
                throw new NotImplementedException();
            }

            return (viewModel, parameters.ContainsKey("contract") ? parameters["contract"] : null);
        }

        /// <inheritdoc/>
#pragma warning disable SA1202 // Elements should be ordered by access
#pragma warning disable CA1063 // Implement IDisposable Correctly
        public void Dispose()
#pragma warning restore CA1063 // Implement IDisposable Correctly
#pragma warning restore SA1202 // Elements should be ordered by access
        {
            var i = 3;
        }
    }
}
