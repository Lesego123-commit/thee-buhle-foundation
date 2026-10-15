// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Google (2026) Material Design 3.
//    - Republic of South Africa (2005) Children's Act 38 of 2005.
// =============================================================================

package za.co.theebuhlefoundation.ui.checkin

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.appcompat.app.AlertDialog
import androidx.fragment.app.Fragment
import androidx.fragment.app.viewModels
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.lifecycleScope
import androidx.lifecycle.repeatOnLifecycle
import androidx.navigation.fragment.findNavController
import com.google.android.material.chip.Chip
import com.google.android.material.chip.ChipGroup
import kotlinx.coroutines.launch
import za.co.theebuhlefoundation.R
import za.co.theebuhlefoundation.databinding.FragmentCheckInBinding
import za.co.theebuhlefoundation.ui.common.ContainerViewModelFactory
import za.co.theebuhlefoundation.ui.common.container

class CheckInFragment : Fragment() {

    private var _binding: FragmentCheckInBinding? = null
    private val binding get() = _binding!!

    private val assignmentId: String
        get() = requireArguments().getString("assignmentId").orEmpty()

    private val learnerName: String
        get() = requireArguments().getString("learnerName").orEmpty()

    private val viewModel: CheckInViewModel by viewModels {
        ContainerViewModelFactory(container) { CheckInViewModel(it.mentor, assignmentId) }
    }

    /**
     * The vocabulary, exactly as the API recognises it.
     *
     * These strings are sent verbatim. An area the server does not recognise is
     * discarded before routing, which once meant a concern tagged "abuse" was
     * silently dropped — so the values here are the API's, and only the labels
     * are for reading.
     */
    private val supportAreas = listOf(
        "home_situation" to "Home situation",
        "safety" to "Safety",
        "wellbeing" to "Wellbeing",
        "bullying" to "Bullying",
        "attendance" to "Attendance",
        "transport" to "Transport",
        "euclidean_geometry" to "Maths topic",
        "exam_technique" to "Exam technique",
        "confidence" to "Confidence",
        "motivation" to "Motivation",
        "study_habits" to "Study habits",
        "subject_choice" to "Subject choice",
        "career_guidance" to "Career guidance",
    )

    private val wellbeings = listOf(
        "Thriving" to "Thriving",
        "Steady" to "Steady",
        "Unsettled" to "Unsettled",
        "Concerned" to "Concerned",
    )

    private val modes = listOf(
        "InPerson" to "In person",
        "Phone" to "Phone",
        "Message" to "Message",
        "Group" to "Group",
    )

    override fun onCreateView(
        inflater: LayoutInflater,
        parent: ViewGroup?,
        savedInstanceState: Bundle?,
    ): View {
        _binding = FragmentCheckInBinding.inflate(inflater, parent, false)

        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        binding.learnerName.text = learnerName

        fill(binding.wellbeingChips, wellbeings, checkable = true, singleChoice = true, preselect = 1)
        fill(binding.modeChips, modes, checkable = true, singleChoice = true, preselect = 0)
        fill(binding.areaChips, supportAreas, checkable = true, singleChoice = false)

        binding.saveButton.setOnClickListener { save() }

        viewLifecycleOwner.lifecycleScope.launch {
            viewLifecycleOwner.repeatOnLifecycle(Lifecycle.State.STARTED) {
                viewModel.state.collect { render(it) }
            }
        }
    }

    private fun save() {
        val wellbeing = selected(binding.wellbeingChips, wellbeings) ?: "Steady"
        val mode = selected(binding.modeChips, modes) ?: "InPerson"
        val areas = allSelected(binding.areaChips, supportAreas)

        viewModel.save(
            wellbeing = wellbeing,
            mode = mode,
            supportAreas = areas,
            notes = binding.notesInput.text?.toString().orEmpty(),
            flagged = binding.flagCheck.isChecked,
        )
    }

    private fun render(state: CheckInState) {
        binding.saveButton.isEnabled = !state.busy
        binding.progress.visibility = if (state.busy) View.VISIBLE else View.GONE

        state.error?.let { message ->
            AlertDialog.Builder(requireContext())
                .setTitle(
                    if (state.unroutable) R.string.check_in_not_saved
                    else R.string.problem_title
                )
                .setMessage(message)
                // Nothing is dismissed for them and the form keeps what they
                // typed: on the unroutable path the concern was NOT saved, and
                // a mentor who taps past this needs to be able to read it again
                // and phone.
                .setPositiveButton(R.string.understood, null)
                .show()

            return
        }

        if (state.saved) {
            AlertDialog.Builder(requireContext())
                .setTitle(
                    if (state.escalated) R.string.check_in_escalated
                    else R.string.check_in_saved
                )
                .setMessage(state.message)
                .setCancelable(false)
                .setPositiveButton(R.string.understood) { _, _ -> findNavController().popBackStack() }
                .show()
        }
    }

    private fun fill(
        group: ChipGroup,
        values: List<Pair<String, String>>,
        checkable: Boolean,
        singleChoice: Boolean,
        preselect: Int? = null,
    ) {
        group.isSingleSelection = singleChoice

        values.forEachIndexed { index, (value, label) ->
            val chip = Chip(requireContext()).apply {
                text = label
                tag = value
                isCheckable = checkable
                isChecked = preselect == index
            }

            group.addView(chip)
        }
    }

    private fun selected(group: ChipGroup, values: List<Pair<String, String>>): String? =
        group.children().firstOrNull { it.isChecked }?.tag as? String

    private fun allSelected(group: ChipGroup, values: List<Pair<String, String>>): List<String> =
        group.children().filter { it.isChecked }.mapNotNull { it.tag as? String }

    private fun ChipGroup.children(): List<Chip> =
        (0 until childCount).mapNotNull { getChildAt(it) as? Chip }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }
}
