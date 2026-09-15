<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="Login.aspx.cs" Inherits="EWFM.AwacsMes.Login" %>
<!DOCTYPE html>
<html lang="en">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>Sign in - ATCB Assembly Recipe</title>
    <style type="text/css">
        /* One embedded stylesheet rather than a linked .css file: this project
           publishes as a bare web service folder with no Content directory, so a
           linked file is one more thing that can 404 after a deploy and leave the
           page unstyled. Move it out when a second page needs the same rules. */

        /* Colours are written out rather than held in CSS custom properties:
           the shop floor PCs still run browsers that ignore var(), and an
           ignored var() renders this page as black text on white with no
           brand bar at all. The palette is
             #12263f brand bar   #0057b8 action   #00468f action hover
             #1c2530 text        #5b6676 muted text
             #d7dde5 borders     #b3261e error
           Replace the first two with the exact ATCB style guide hex values. */

        html, body {
            margin: 0;
            padding: 0;
            height: 100%;
            background: #eef1f5;
            color: #1c2530;
            font-family: "Segoe UI", Tahoma, Arial, sans-serif;
            font-size: 14px;
        }

        /* Brand bar. Sits at the top of the page, above the sign-in card. */
        .brandbar {
            background: #12263f;
            padding: 14px 24px;
        }

        .brandbar .wordmark {
            color: #ffffff;
            font-size: 20px;
            font-weight: 600;
            letter-spacing: 0.02em;
            /* The Nexperia wordmark is set in lower case. Swap this span for an
               <img src="nexperia-logo.svg" alt="Nexperia" /> once the official
               asset is dropped into the site folder. */
            text-transform: lowercase;
        }

        .page {
            display: block;
            padding: 48px 16px 24px 16px;
        }

        .card {
            width: 100%;
            max-width: 360px;
            margin: 0 auto;
            background: #ffffff;
            border: 1px solid #d7dde5;
            border-radius: 6px;
            padding: 28px 28px 24px 28px;
            box-sizing: border-box;
            box-shadow: 0 1px 3px rgba(18, 38, 63, 0.08);
        }

        .app-title {
            margin: 0;
            font-size: 19px;
            font-weight: 600;
            line-height: 1.3;
        }

        /* "Sign in" heading - deliberately regular weight, not bold. */
        .signin-heading {
            margin: 4px 0 20px 0;
            font-size: 15px;
            font-weight: 400;
            color: #5b6676;
        }

        .field {
            margin-bottom: 14px;
        }

        .field label {
            display: block;
            margin-bottom: 5px;
            font-size: 13px;
            color: #5b6676;
        }

        .field input[type="text"],
        .field input[type="password"] {
            width: 100%;
            box-sizing: border-box;
            padding: 8px 10px;
            border: 1px solid #d7dde5;
            border-radius: 4px;
            font-family: inherit;
            font-size: 14px;
            color: #1c2530;
            background: #ffffff;
        }

        .field input[type="text"]:focus,
        .field input[type="password"]:focus {
            outline: none;
            border-color: #0057b8;
            box-shadow: 0 0 0 2px rgba(0, 87, 184, 0.15);
        }

        /* Small sign-in button: sized to its own text, not the card width. */
        .btn-signin {
            display: inline-block;
            padding: 6px 18px;
            border: 1px solid #0057b8;
            border-radius: 4px;
            background: #0057b8;
            color: #ffffff;
            font-family: inherit;
            font-size: 13px;
            font-weight: 400;
            line-height: 1.4;
            cursor: pointer;
        }

        .btn-signin:hover {
            background: #00468f;
            border-color: #00468f;
        }

        .actions {
            margin-top: 18px;
        }

        .error {
            display: block;
            margin-bottom: 16px;
            padding: 8px 10px;
            border: 1px solid #e6b4b0;
            border-radius: 4px;
            background: #fdf2f1;
            color: #b3261e;
            font-size: 13px;
        }

        .footnote {
            max-width: 360px;
            margin: 14px auto 0 auto;
            color: #5b6676;
            font-size: 12px;
            text-align: center;
        }
    </style>
</head>
<body>
    <form id="formLogin" runat="server" defaultbutton="btnSignIn" defaultfocus="txtUserId">
    <div class="brandbar">
        <span class="wordmark">Nexperia</span>
    </div>

    <div class="page">
        <div class="card">
            <h1 class="app-title">ATCB Assembly Recipe</h1>

            <asp:PlaceHolder ID="phSignIn" runat="server">
                <p class="signin-heading">Sign in</p>

                <asp:Label ID="lblError" runat="server" CssClass="error" Visible="false" EnableViewState="false" />

                <div class="field">
                    <asp:Label ID="lblUserId" runat="server" AssociatedControlID="txtUserId" Text="User ID" />
                    <asp:TextBox ID="txtUserId" runat="server" MaxLength="64" autocomplete="username" />
                </div>

                <div class="field">
                    <asp:Label ID="lblPassword" runat="server" AssociatedControlID="txtPassword" Text="Password" />
                    <asp:TextBox ID="txtPassword" runat="server" TextMode="Password" MaxLength="128" autocomplete="current-password" />
                </div>

                <div class="actions">
                    <asp:Button ID="btnSignIn" runat="server" CssClass="btn-signin" Text="Sign in" OnClick="btnSignIn_Click" />
                </div>
            </asp:PlaceHolder>

            <!-- Shown only when someone is already signed in and there is no page
                 to send them on to yet. See Page_Load. -->
            <asp:PlaceHolder ID="phSignedIn" runat="server" Visible="false">
                <p class="signin-heading">Signed in as <asp:Literal ID="litUserName" runat="server" /></p>

                <div class="actions">
                    <asp:Button ID="btnSignOut" runat="server" CssClass="btn-signin" Text="Sign out" OnClick="btnSignOut_Click" />
                </div>
            </asp:PlaceHolder>
        </div>

        <p class="footnote">Use your Nexperia network account.</p>
    </div>
    </form>
</body>
</html>
