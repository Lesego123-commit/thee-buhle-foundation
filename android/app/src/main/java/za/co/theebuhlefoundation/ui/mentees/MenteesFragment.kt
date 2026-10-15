// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Google (2026) Material Design 3.
// =============================================================================

package za.co.theebuhlefoundation.ui.mentees

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.core.os.bundleOf
import androidx.fragment.app.Fragment
import androidx.fragment.app.viewModels
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.lifecycleScope
import androidx.lifecycle.repeatOnLifecycle
import androidx.navigation.fragment.findNavController
import androidx.recyclerview.widget.LinearLayoutManager
import kotlinx.coroutines.launch
import za.co.theebuhlefoundation.R
import za.co.theebuhlefoundation.data.api.dto.MenteeCard
import za.co.theebuhlefoundation.databinding.FragmentListBinding
import za.co.theebuhlefoundation.databinding.ItemMenteeBinding
import za.co.theebuhlefoundation.ui.common.BindingAdapter
import za.co.theebuhlefoundation.ui.common.ContainerViewModelFactory
import za.co.theebuhlefoundation.ui.common.StateViews
import za.co.theebuhlefoundation.ui.common.container

class MenteesFragment : Fragment() {

    private var _binding: FragmentListBinding? = null
    private val binding get() = _binding!!

    private lateinit var state: StateViews

    private val viewModel: MenteesViewModel by viewModels {
        ContainerViewModelFactory(container) { MenteesViewModel(it.mentor) }
    }

    private val adapter = BindingAdapter<MenteeCard, ItemMenteeBinding>(
        inflate = ItemMenteeBinding::inflate,
        identity = { it.assignmentId },
    ) { binding, mentee, _ ->
        val context = binding.root.context

        binding.menteeName.text = mentee.fullName
        binding.menteeMeta.text = listOfNotNull(
            mentee.referenceCode,
            context.getString(R.string.mentee_grade, mentee.grade),
            mentee.suburb,
        ).joinToString(" · ")

        binding.menteeLastSeen.text = when (mentee.daysSince) {
            null -> context.getString(R.string.mentee_never)
            0 -> context.getString(R.string.mentee_today)
            else -> context.resources.getQuantityString(
                R.plurals.mentee_days_ago,
                mentee.daysSince,
                mentee.daysSince,
            )
        }

        binding.menteeOverdue.visibility = if (mentee.isOverdue) View.VISIBLE else View.GONE

        binding.root.setOnClickListener {
            findNavController().navigate(
                R.id.toCheckIn,
                bundleOf(
                    "assignmentId" to mentee.assignmentId,
                    "learnerName" to mentee.fullName,
                ),
            )
        }
    }

    override fun onCreateView(
        inflater: LayoutInflater,
        parent: ViewGroup?,
        savedInstanceState: Bundle?,
    ): View {
        _binding = FragmentListBinding.inflate(inflater, parent, false)

        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        state = StateViews(binding.stateHost, binding.content)
        state.onRetry { viewModel.load() }

        binding.listTitle.text = getString(R.string.mentees_title)
        binding.list.layoutManager = LinearLayoutManager(requireContext())
        binding.list.adapter = adapter
        binding.refresh.setOnRefreshListener { viewModel.load(refreshing = true) }

        viewLifecycleOwner.lifecycleScope.launch {
            viewLifecycleOwner.repeatOnLifecycle(Lifecycle.State.STARTED) {
                viewModel.state.collect { current ->
                    binding.refresh.isRefreshing = false
                    state.render(current) { adapter.submitList(it.mentees) }
                }
            }
        }
    }

    override fun onResume() {
        super.onResume()

        // Reloaded on resume rather than only on create: coming back from a
        // check-in should show the learner as seen today, not as overdue.
        viewModel.load(refreshing = true)
    }

    override fun onDestroyView() {
        super.onDestroyView()
        binding.list.adapter = null
        _binding = null
    }
}
