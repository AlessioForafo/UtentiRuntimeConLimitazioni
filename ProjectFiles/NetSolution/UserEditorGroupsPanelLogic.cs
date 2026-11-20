#region Using directives
using FTOptix.Core;
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using FTOptix.OPCUAServer;
using FTOptix.UI;
using System.Collections.Generic;
using System.Linq;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
#endregion

public class UserEditorGroupsPanelLogic : BaseNetLogic
{
    /// <summary>
    /// This method handles the start of the operation by initializing variables, 
    /// subscribing to variable change events, updating groups, and setting up the UI.
    /// </summary>
    /// <remarks>
    /// The method ensures that the UI is properly set up and that variable changes are tracked.
    /// </remarks>
    public override void Start()
    {
        userVariable = Owner.GetVariable("User");
        editable = Owner.GetVariable("Editable");

        userVariable.VariableChange += UserVariable_VariableChange;
        editable.VariableChange += Editable_VariableChange;

        UpdateGroupsAndUser();

        BuildUIGroups();
        if (editable.Value)
            SetCheckedValues();
    }

    /// <summary>
    /// This method handles the change in a variable value, updating the groups and UI components, and setting checked values if the new value is true.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The VariableChangeEventArgs instance containing the event data.</param>
    private void Editable_VariableChange(object sender, VariableChangeEventArgs e)
    {
        UpdateGroupsAndUser();
        BuildUIGroups();

        if (e.NewValue)
            SetCheckedValues();
    }

    /// <summary>
    /// This method handles the variable change event, updating the groups and user state based on the event.
    /// If the 'editable' value is true, it sets the checked values; otherwise, it builds the UI groups.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The VariableChangeEventArgs instance containing the event data.</param>
    private void UserVariable_VariableChange(object sender, VariableChangeEventArgs e)
    {
        UpdateGroupsAndUser();
        if (editable.Value)
            SetCheckedValues();
        else
            BuildUIGroups();
    }

    /// <summary>
    /// Updates the groups and user based on the provided variable.
    /// </summary>
    /// <remarks>
    /// The method assumes that <see cref="userVariable.Value.Value" /> is not null.
    /// </remarks>
    private void UpdateGroupsAndUser()
    {
        if (userVariable.Value.Value != null)
            user = InformationModel.Get(userVariable.Value);

        groups = LogicObject.GetAlias("Groups");
    }

    /// <summary>
    /// This method creates a ColumnLayout container and adds UI components for each group.
    /// It handles different types of group UI objects based on the `editable` value and whether the user has access to the group.
    /// The method sets up the layout, adjusts margins, and adds the UI objects to a scrollable panel.
    /// </summary>
    /// <remarks>
    /// The method checks if `groups` is null and returns early if so. It then creates a ColumnLayout container with stretch alignment and top margin.
    /// For each group in `groups.Children`, it creates a UI object based on the `editable` flag and group access.
    /// The UI objects are added to the panel, and the panel's height is updated to accommodate them.
    /// Finally, the panel is added to a scroll view if available.
    /// </remarks>
    private void BuildUIGroups()
    {
        if (groups == null)
            return;

        panel?.Delete();


        //********************** controllo gruppi utente loggato in quel momento ********************************
        //var loggedUser = Session.User;
        //var userGroups = loggedUser.Refs.GetObjects(FTOptix.Core.ReferenceTypes.HasGroup, false);
        
        //List<string> listaGruppiUtenteAttivo = new List<string>();     
        //foreach (var userGroup in userGroups)
        //{            
        //    listaGruppiUtenteAttivo.Add(userGroup.BrowseName);
        //}
        //*******************************************************************************************************

        //creazione dizionario per gestire gruppi in maniera gerarchica
        Dictionary<string, int> groupLevels = new()
        {
            { "Operator", 1 },
            { "Maintanance", 2 },
            { "Supervisor", 3 },
            { "Administrator", 4 }
        };

        //recupero la lista dei gruppi a cui appartiene l'utente collegato
        //Se un utente appartiene a più gruppi, considero il più alto:
        var loggedUser = Session.User;
        var loggedUserGroups = loggedUser.Refs.GetObjects(FTOptix.Core.ReferenceTypes.HasGroup, false);

        if (loggedUserGroups == null || !loggedUserGroups.Any())
        {
            Log.Warning("Security", "L'utente non appartiene ad alcun gruppo");
            return;
        }
        int loggedUserLevel = loggedUserGroups.Select(g => groupLevels[g.BrowseName]).Max();

        //*******************************************************************************************************

        panel = InformationModel.MakeObject<ColumnLayout>("Container");
        panel.HorizontalAlignment = HorizontalAlignment.Stretch;
        panel.VerticalAlignment = VerticalAlignment.Top;
        panel.TopMargin = 0;

        foreach (var group in groups.Children)
        {
            // Ignora gruppi non presenti nel dizionario
            if (!groupLevels.TryGetValue(group.BrowseName, out int groupLevel))
                continue;

            // Mostra solo gruppi con livello <= utente loggato
            if (groupLevel > loggedUserLevel)
                continue;

            Panel groupUiObject = null;

            if (editable.Value)
            {
                groupUiObject = InformationModel.MakeObject<Panel>(
                    group.BrowseName,
                    UtentiRuntimeConLimitazioni.ObjectTypes.GroupCheckbox);
            }
            else if (UserHasGroup(group.NodeId))
            {
                groupUiObject = InformationModel.MakeObject<Panel>(
                    group.BrowseName,
                    UtentiRuntimeConLimitazioni.ObjectTypes.GroupLabel);
            }

            if (groupUiObject == null)
                continue;

            groupUiObject.HorizontalAlignment = HorizontalAlignment.Stretch;
            groupUiObject.VerticalAlignment = VerticalAlignment.Top;
            groupUiObject.TopMargin = 0;
            groupUiObject.GetVariable("Group").Value = group.NodeId;

            panel.Add(groupUiObject);
            panel.Height += groupUiObject.Height;
        }

        var scrollView = Owner.Get("ScrollView");
        scrollView?.Add(panel);
    }

    /// <summary>
    /// This method sets the checked status of checkboxes in a panel based on a group identifier.
    /// It retrieves all checkboxes in the panel and updates their "Checked" variable based on the
    /// result of <see cref="UserHasGroup"/> for the corresponding group.
    /// </summary>
    /// <remarks>
    /// The method first checks if the <see cref="groups"} and <see cref="panel"} are not null.
    /// It then retrieves all checkboxes in the panel using the <see cref="OpcUa.ReferenceTypes.HasOrderedComponent"} reference type.
    /// For each checkbox, it retrieves the group it belongs to and sets its "Checked" value
    /// using the <see cref="UserHasGroup"/> method.
    /// </remarks>
    private void SetCheckedValues()
    {
        if (groups == null)
            return;

        if (panel == null)
            return;

        var groupCheckBoxes = panel.Refs.GetObjects(OpcUa.ReferenceTypes.HasOrderedComponent, false);

        foreach (var groupCheckBoxNode in groupCheckBoxes)
        {
            var group = groups.Get(groupCheckBoxNode.BrowseName);
            groupCheckBoxNode.GetVariable("Checked").Value = UserHasGroup(group.NodeId);
        }
    }

    /// <summary>
    /// This method checks if the user has a group with the specified NodeId.
    /// </summary>
    /// <param name="groupNodeId">The NodeId of the group to check.</param>
    /// <returns>
    /// A boolean value indicating whether the user has the specified group.
    /// </returns>
    private bool UserHasGroup(NodeId groupNodeId)
    {
        if (user == null)
            return false;
        var userGroups = user.Refs.GetObjects(FTOptix.Core.ReferenceTypes.HasGroup, false);
        foreach (var userGroup in userGroups)
        {            
            if (userGroup.NodeId == groupNodeId)
                return true;
        }
        return false;
    }

    private IUAVariable userVariable;
    private IUAVariable editable;

    private IUANode groups;
    private IUANode user;
    private ColumnLayout panel;
}
