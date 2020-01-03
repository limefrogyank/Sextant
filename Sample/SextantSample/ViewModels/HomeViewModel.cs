using System;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;
using Sextant;
using Splat;

namespace SextantSample.ViewModels
{
    public class HomeViewModel : ViewModelBase
    {
        public override string Id => nameof(HomeViewModel);

        public ReactiveCommand<Unit, Unit> OpenModal { get; set; }

        public ReactiveCommand<Unit, Unit> PushPage { get; set; }

        public ReactiveCommand<Unit, Unit> PushGenericPage { get; set; }

        public ReactiveCommand<Unit, Unit> ViewStackTop { get; set; }

        public HomeViewModel()
            : base(Locator.Current.GetService<IViewStackService>())
        {
            OpenModal = ReactiveCommand
                .CreateFromObservable(() =>
                    ViewStackService.PushModal(new FirstModalViewModel(ViewStackService)),
                    outputScheduler: RxApp.MainThreadScheduler);

            PushPage = ReactiveCommand
                .CreateFromObservable(() =>
                    ViewStackService.PushPage(new RedViewModel(ViewStackService)),
                    outputScheduler: RxApp.MainThreadScheduler);

            PushGenericPage = ReactiveCommand
                .CreateFromObservable(() =>
                        ViewStackService.PushPage<GreenViewModel>(),
                    outputScheduler: RxApp.MainThreadScheduler);

            ViewStackTop = ReactiveCommand.Create(() =>
            {
                ViewStackService.TopPage().Subscribe(vm => Interactions.ErrorMessage.Handle(new Exception($"The top of the stack contains a viewmodel of type, {vm.GetType()}.")).Subscribe());
                return Unit.Default;
            });

            PushPage.ThrownExceptions.Subscribe(error => Interactions.ErrorMessage.Handle(error).Subscribe());
            PushGenericPage.ThrownExceptions.Subscribe(error => Interactions.ErrorMessage.Handle(error).Subscribe());
            OpenModal.ThrownExceptions.Subscribe(error => Interactions.ErrorMessage.Handle(error).Subscribe());
        }
    }
}
