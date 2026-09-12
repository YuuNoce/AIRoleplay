# AIRoleplay

[日本語版](README.ja.md)

AIRoleplay is a Dalamud plugin that uses AI to create roleplay replies for FFXIV's in-game chat.

The AI creates replies based on recent in-game chat and the character profile you configure. Supported AI services are OpenAI, Claude, Gemini, DeepSeek, and Z.ai.

## Features

* A Private Mode that lets you play while talking with an AI character
* A Public Mode that creates messages addressed to other players in the game
* Reads recent in-game chat and creates replies that follow the flow of the conversation
* Creates drafts for Say, Party, Free Company, and Tell, then lets you post them after confirmation
* Per-character roleplay profiles, including personality and speaking style
* Up to three fallback models in case an AI model fails
* Conversation log storage for troubleshooting

## Installation

In Dalamud Settings, open "Experimental" and add the following URL under "Custom Plugin Repositories":

```text
https://raw.githubusercontent.com/YuuNoce/AIRoleplay/master/repo.json
```

Save the settings, then search for "AIRoleplay" in the Plugin Installer and install it.

## First, understand Private Mode and Public Mode

This plugin has two modes.

* **Private Mode** lets you play while talking with an AI character. There are three ways to talk to it:

  1. Type directly in the AIRoleplay window
  2. Type in the game's Say input
  3. Use the \`/airp message\` command

  Only method 2 makes your own message visible to other players as an in-game Say message. AIRoleplay simply reads that message as conversation context. Methods 1 and 3 do not display anything in the in-game chat.

  **No matter which method you use, other players cannot see the AI's response.** It appears in the AIRoleplay window and can also be displayed in your in-game chat log if enabled in the settings, but that display is visible only on your own client and is not a chat post.

* **Public Mode** asks the AI to compose messages addressed to other players in the game. Messages created here can be posted to the game as Say, Party, FC, or Tell messages. **Direct Posting automatically posts generated text to the game without a review or confirmation step.**

The plugin always returns to Private Mode when it starts, when you log out, or when you change characters. This is a safety measure to prevent accidentally leaving Public Mode enabled and posting an unintended message in front of other players.

## Commands

Enter \`/airp\` to open the main window. To give instructions directly without opening the window first, use the following commands:

\`\`\`text
/airp                          → Open the main window
/airp hello                    → Send a message to the AI in the current mode
/airp r [name] [instruction]   → Switch to Reply Assist and create a reply for the specified player
/airp r <t> [instruction]      → Create a reply for your current target
/airp target current|party|none|"Name@World"  → Set the Reply Assist recipient
/airp channel say|party|fc|tell → Set the destination channel
/airp status                   → Show the current settings and status
/airp help                     → Show the command list
\`\`\`

## Initial setup: Register an LLM API key

This plugin does not store LLM API keys. It reads them only when making a request. **Neither AIRoleplay nor its distributor provides API keys.** Register an LLM API key that you obtained and own in Windows.

1. Search Windows for "Credential Manager" and open it
2. Select "Windows Credentials"
3. Click "Add a generic credential"
4. Enter the values shown below for the AI service you want to use

| AI service | Internet or network address | User name |
| --- | --- | --- |
| OpenAI | \`AIRoleplay/OpenAI\` | \`AIRoleplay\` |
| Claude | \`AIRoleplay/Claude\` | \`AIRoleplay\` |
| Gemini | \`AIRoleplay/Gemini\` | \`AIRoleplay\` |
| DeepSeek | \`AIRoleplay/DeepSeek\` | \`AIRoleplay\` |
| Z.ai | \`AIRoleplay/Zai\` | \`AIRoleplay\` |

Enter the LLM API key you own in the "Password" field.

After registering it, open the plugin settings, select the "LLM Models" tab, and click "Reload key status."

## The difference between the reply recipient and destination channel (important)

These names may sound similar, but they represent completely different settings. Confusing them can cause problems.

* **Reply recipient** tells the AI who the message should be addressed to
* **Destination channel** determines which channel the message is actually sent to

For example:

> Even if you set the reply recipient to a particular player, if the destination channel is still Say, everyone nearby can see the message—not just that player.

**To send a message privately**, specify the reply recipient and also **explicitly change the destination channel to Tell**.

**If no captured messages from the reply recipient are found** and the plugin displays "No captured speech from this player," you can still create a message manually. The AI is told only that the message is addressed to that player.

## Sending a Tell (private message)

Tell can be used only when the destination can be resolved to one specific player. It cannot be used while "Whole conversation" or "Party" is selected.

There are three ways to specify the destination:

| Method | Description |
| --- | --- |
| Select a recent speaker | Uses information for a player recorded in the captured chat log |
| Follow the current target | Uses the player you are currently targeting; unavailable for NPCs or when no target is selected |
| Enter manually | Must be entered precisely as \`Name@World\`; the World cannot be omitted |

If the destination cannot be resolved or the input format is invalid, processing stops with an error. The plugin does not fall back to another channel, such as sending via Say when a Tell cannot be sent.

When sending a Tell to a player on another World, FFXIV restrictions may apply depending on circumstances such as friend status or Party Finder participation. See the [official FFXIV text command list](https://na.finalfantasyxiv.com/lodestone/playguide/db/text_command/) for details.

When this plugin displays "Sent," it means that it requested the game to send the message. It does not guarantee that the message was delivered to the recipient.

## Replying to the whole party

"Party" creates a reply to the overall flow of party chat rather than to one specific player.

* Party messages are automatically included in the captured context
* The destination channel is automatically fixed to Party

## Using Reply Assist with the game input

In Reply Assist mode, text in the plugin's editing area is reflected directly in the game's chat input.

* Line breaks are automatically replaced with spaces
* The message is sent only when you press Enter in the game
* Editing or clearing the game's input directly stops the link
* Changing the mode, destination channel, or recipient cancels the current generation

Text containing \`<\` or \`>\` is not inserted or sent, to avoid display issues in the game.

The character profile used in Public Mode, including personality and speaking style, is stored at \`AIRoleplay/characters/Player Name@Home World/profile.txt\`. Loading text from the settings and saving it updates this file.

## Remembering longer conversations with conversation summaries

As a conversation with the AI continues, the AI may forget what was discussed. The "Use an AI-generated conversation summary" option is designed to prevent this.

It is disabled by default. When enabled, each reply generates both the current message and an updated conversation summary. Only the message is shown on screen, while the summary is retained internally.

Summaries are cleared when you log out, change characters, reload the plugin, or use "Reset internal summaries" in the settings. **Private and Public summaries are managed separately.** In Public Mode, summaries are stored separately for each combination of destination channel and reply recipient, and for each \`Name@World\` in Tell.

## Saving logs

Log storage is off by default. You can enable it and choose the destination folder from the conversation window or settings. User-facing logs are stored under a \`Player Name@Home World/\` folder as \`private.log\` for **Private Mode** and \`public.log\` for Public Mode. Full prompts and API diagnostics are not normally saved.
