#!/bin/bash

FILE="/Users/Kim/Library/CloudStorage/Dropbox/documenten/Rhea/Vidyano/Vidyano-dotnet/Tools/TerminalHost/src/TerminalHost/TerminalHost/Views/SettingsView.axaml"

# 1. Replace Style="{StaticResource ...}" with Classes="..."
# This is the most complex change - we need to extract the resource name and use it as a class

# For inline Style attributes on controls
sed -i '' -E 's/Style="\{StaticResource ([^}]+)\}"/Classes="\1"/g' "$FILE"

# 2. Replace Visibility="{Binding ...}" with IsVisible="{Binding ...}"
sed -i '' -E 's/Visibility="\{Binding/IsVisible="{Binding/g' "$FILE"

# 3. Remove TargetType from Style definitions that have Selector
# In Avalonia, when you have a Selector, you don't need TargetType
sed -i '' -E 's/ Selector="([^"]+)" TargetType="[^"]+"/ Selector="\1"/g' "$FILE"

# 4. Replace ItemContainerStyle with Styles.Resources for ListBox items
# This is more complex and may need manual intervention

echo "Basic fixes applied. Manual verification needed for:"
echo "1. ItemContainerStyle usage - may need Styles property instead"
echo "2. Converter parameters that expect Visibility type"
echo "3. Any remaining Style attributes"
