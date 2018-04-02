﻿namespace GWallet.Frontend.XF

open System
open System.Linq
open System.Timers
open System.Threading.Tasks

open Xamarin.Forms
open Xamarin.Forms.Xaml
open Plugin.Clipboard

open GWallet.Backend

type BalancesPage() as this =
    inherit ContentPage()

    let _ = base.LoadFromXaml(typeof<BalancesPage>)

    let mainLayout = base.FindByName<StackLayout>("mainLayout")
    let normalAccounts = GWallet.Backend.Account.GetAllActiveAccounts().OfType<NormalAccount>() |> List.ofSeq

    let timeToRefreshBalances = TimeSpan.FromSeconds 60.0
    let balanceRefreshTimer = new Timer(timeToRefreshBalances.TotalMilliseconds)

    let CreateWidgetsForAccount(account: NormalAccount): Label*Button =
        let accountBalanceLabel = Label(Text = "...")
        let sendButton = Button(Text = "Send",
                                //FIXME: rather enable it always, and give error when balance is not fresh
                                IsEnabled = false)
        accountBalanceLabel,sendButton

    let accountsAndBalances: List<NormalAccount*Label*Button> =
        seq {
            for normalAccount in normalAccounts do
                let label,button = CreateWidgetsForAccount normalAccount
                yield normalAccount,label,button
        } |> List.ofSeq

    do
        this.Init()

    member this.StartTimer() =
        balanceRefreshTimer.Elapsed.Add (fun _ ->
            for normalAccount,accountBalance,sendButton in accountsAndBalances do
                let balance = Account.GetShowableBalance normalAccount
                                  |> Async.RunSynchronously
                let account = normalAccount :> IAccount
                match balance with
                | Fresh(amount) ->
                    Device.BeginInvokeOnMainThread(fun _ ->
                        if (amount > 0.0m) then
                            sendButton.IsEnabled <- true
                        accountBalance.Text <- sprintf "%s %s" (amount.ToString()) (account.Currency.ToString())
                    )
                | _ -> ()
        )
        balanceRefreshTimer.Start()

    member this.PopulateGrid (initialBalancesTasksWithDetails: seq<_*NormalAccount*Label*Button>) =
        let grid = Grid(HorizontalOptions = LayoutOptions.FillAndExpand, VerticalOptions = LayoutOptions.FillAndExpand)

        let columnDef1 = ColumnDefinition()
        grid.ColumnDefinitions.Add(columnDef1)
        let columnDef2 = ColumnDefinition()
        grid.ColumnDefinitions.Add(columnDef2)
        let columnDef3 = ColumnDefinition()
        grid.ColumnDefinitions.Add(columnDef3)

        mainLayout.Children.Remove(mainLayout.FindByName<Label>("loadingLabel")) |> ignore
        mainLayout.Children.Add(grid)
        let mutable rowCount = 0 //TODO: do this recursively instead of imperatively
        for _,normalAccount,accountBalance,sendButton in initialBalancesTasksWithDetails do
            let account = normalAccount :> IAccount

            let rowDefinition = RowDefinition()
            grid.RowDefinitions.Add(rowDefinition)

            sendButton.Clicked.Subscribe(fun _ ->
                this.Navigation.PushModalAsync(SendPage(normalAccount)) |> FrontendHelpers.DoubleCheckCompletion
            ) |> ignore

            let receiveButton = Button(Text = "Receive")
            receiveButton.Clicked.Subscribe(fun _ ->
                // no support for visualizing QR codes in Mac yet, but at least support for clipboard's "copy"
                if (Device.RuntimePlatform = Device.macOS ||
                    Device.RuntimePlatform = Device.GTK) then
                    FrontendHelpers.ChangeTextAndChangeBack receiveButton "Copied"

                    CrossClipboard.Current.SetText account.PublicAddress
                else
                    this.Navigation.PushModalAsync(ReceivePage(normalAccount))
                        |> FrontendHelpers.DoubleCheckCompletion
            ) |> ignore

            // TODO: add a "List transactions" button using Device.OpenUri() with etherscan, gastracker, etc

            accountBalance.HorizontalOptions <- LayoutOptions.End
            accountBalance.VerticalOptions <- LayoutOptions.Center
            grid.Children.Add(accountBalance, 0, rowCount)

            sendButton.HorizontalOptions <- LayoutOptions.Center
            sendButton.VerticalOptions <- LayoutOptions.Center
            grid.Children.Add(sendButton, 1, rowCount)

            receiveButton.HorizontalOptions <- LayoutOptions.Start
            receiveButton.VerticalOptions <- LayoutOptions.Center
            grid.Children.Add(receiveButton, 2, rowCount)
            rowCount <- rowCount + 1

// idea taken from: https://stackoverflow.com/a/31456367/544947
#if DEBUG_LAYOUT
            accountBalance.BackgroundColor <- Color.Gray
            receiveButton.BackgroundColor <- Color.Beige
        grid.BackgroundColor <- Color.Brown
        if (grid.ColumnSpacing = 0) then
            grid.ColumnSpacing <- 0.5
        if (grid.RowSpacing = 0) then
            grid.RowSpacing <- 0.5
#endif


    member this.Init (): unit =

        let initialBalancesTasksWithDetails =
            seq {
                for normalAccount,accountBalanceLabel,sendButton in accountsAndBalances do
                    let account = normalAccount :> IAccount
                    let balanceJob = async {
                        let! balance = Account.GetShowableBalance account
                        let balanceAmount =
                            match balance with
                            | NotFresh(NotAvailable) -> "?"
                            | NotFresh(Cached(amount,_)) -> amount.ToString()
                            | Fresh(amount) ->
                                if (amount > 0.0m) then
                                    sendButton.IsEnabled <- true
                                amount.ToString()
                        accountBalanceLabel.Text <- sprintf "%s %A" balanceAmount account.Currency
                    }
                    yield balanceJob,normalAccount,accountBalanceLabel,sendButton
            }

        let allBalancesJob = Async.Parallel (initialBalancesTasksWithDetails |> Seq.map (fun (j,_,_,_) -> j))
        let populateGrid = async {
            let! _ = allBalancesJob
            Device.BeginInvokeOnMainThread(fun _ ->
                this.PopulateGrid initialBalancesTasksWithDetails
            )
            this.StartTimer()
        }
        Async.StartAsTask populateGrid
            |> FrontendHelpers.DoubleCheckCompletion

        ()

