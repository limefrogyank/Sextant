﻿// Copyright (c) 2019 .NET Foundation and Contributors. All rights reserved.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Reactive.Concurrency;
using System.Text;
using Microsoft.AspNetCore.Components;
using ReactiveUI;
using Splat;

namespace Sextant.Blazor
{
    /// <summary>
    /// Extension methods associated with the IMutableDependencyResolver interface.
    /// </summary>
    public static class DependencyResolverMixins
    {
        /// <summary>
        /// Registers a value for navigation.
        /// </summary>
        /// <param name="dependencyResolver">The dependency resolver.</param>
        /// <param name="viewModelRouter">The viewmodel router.</param>
        /// <returns>The dependencyResolver.</returns>
        public static IMutableDependencyResolver RegisterStackServices(this IMutableDependencyResolver dependencyResolver, IView viewModelRouter)
        {
            var viewStackService = new ViewStackService(viewModelRouter);

            dependencyResolver.RegisterLazySingleton<IViewStackService>(() => viewStackService);
            return dependencyResolver;
        }

        /// <summary>
        /// Initializes Blazor-specific locator.
        /// </summary>
        /// <param name="dependencyResolver">The dependency resolver.</param>
        /// <returns>The dependencyResolver.</returns>
        public static IMutableDependencyResolver RegisterRouteViewViewModelLocator(this IMutableDependencyResolver dependencyResolver)
        {
            dependencyResolver.RegisterConstant(new RouteViewViewModelLocator(), typeof(RouteViewViewModelLocator));
            return dependencyResolver;
        }

        /// <summary>
        /// Initializes Blazor-specific url viewmodel generator.
        /// </summary>
        /// <param name="dependencyResolver">The dependency resolver.</param>
        /// <returns>The dependencyResolver.</returns>
        public static IMutableDependencyResolver RegisterUrlParameterViewModelGenerator(this IMutableDependencyResolver dependencyResolver)
        {
            dependencyResolver.RegisterConstant(new UrlParameterViewModelGenerator(), typeof(UrlParameterViewModelGenerator));
            return dependencyResolver;
        }

        /// <summary>
        /// Register view for viewmodel, but only return view type for UWP frame.
        /// </summary>
        /// <typeparam name="TView">The view type.</typeparam>
        /// <typeparam name="TViewModel">The viewmodel type.</typeparam>
        /// <param name="dependencyResolver">The dependency resolver.</param>
        /// <param name="route">The route (relative url segment).</param>
        /// <param name="contract">The contract.</param>
        /// <returns>
        /// The dependencyResolver.
        /// </returns>
        public static IMutableDependencyResolver RegisterBlazorRoute<TView, TViewModel>(this IMutableDependencyResolver dependencyResolver, string route, string contract = null)
            where TView : IComponent, new()
            where TViewModel : class, IViewModel
        {
            var blazorResolver = Locator.Current.GetService<RouteViewViewModelLocator>();
            blazorResolver.Register<TView, TViewModel>(route, contract);
            return dependencyResolver;
        }

        /// <summary>
        /// Helper method to get view type for viewmodel.
        /// </summary>
        /// <typeparam name="TViewModel">The viewmodel Type.</typeparam>
        /// <param name="dependencyResolver">The dependencyResolver.</param>
        /// <param name="contract">The contract.</param>
        /// <returns>The view Type again.</returns>
        public static Type ResolveView<TViewModel>(this IReadonlyDependencyResolver dependencyResolver, string contract = null)
            where TViewModel : class
        {
            var uwpViewTypeResolver = Locator.Current.GetService<RouteViewViewModelLocator>();
            var viewType = uwpViewTypeResolver.ResolveViewType<TViewModel>(contract);

            // var viewType = (Type)dependencyResolver.GetService<IViewFor<TViewModel>>(contract);
            return viewType;
        }

        /// <summary>
        /// Helper method to get view type for viewmodel.
        /// </summary>
        /// <typeparam name="TViewModel">The viewmodel Type.</typeparam>
        /// <param name="dependencyResolver">The dependencyResolver.</param>
        /// <param name="viewModel">The viewmodel.</param>
        /// <param name="contract">The contract.</param>
        /// <returns>The view Type again.</returns>
        public static Type ResolveView<TViewModel>(this IReadonlyDependencyResolver dependencyResolver, TViewModel viewModel, string contract = null)
            where TViewModel : class, IViewModel
        {
            var vm = viewModel;
            var uwpViewTypeResolver = Locator.Current.GetService<RouteViewViewModelLocator>(contract);
            var viewType = uwpViewTypeResolver.ResolveViewType<TViewModel>();

            return viewType;
        }
    }
}
