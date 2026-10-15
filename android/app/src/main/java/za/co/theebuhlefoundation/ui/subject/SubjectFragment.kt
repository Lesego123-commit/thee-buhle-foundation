// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Google (2026) Material Design 3.
// =============================================================================

package za.co.theebuhlefoundation.ui.subject

import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.fragment.app.Fragment
import androidx.fragment.app.viewModels
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.lifecycleScope
import androidx.lifecycle.repeatOnLifecycle
import androidx.recyclerview.widget.LinearLayoutManager
import com.google.android.material.chip.Chip
import kotlinx.coroutines.launch
import za.co.theebuhlefoundation.R
import za.co.theebuhlefoundation.data.api.dto.ResourceCard
import za.co.theebuhlefoundation.databinding.FragmentSubjectBinding
import za.co.theebuhlefoundation.databinding.ItemResourceBinding
import za.co.theebuhlefoundation.ui.common.BindingAdapter
import za.co.theebuhlefoundation.ui.common.ContainerViewModelFactory
import za.co.theebuhlefoundation.ui.common.StateViews
import za.co.theebuhlefoundation.ui.common.container

class SubjectFragment : Fragment() {

    private var _binding: FragmentSubjectBinding? = null
    private val binding get() = _binding!!

    private lateinit var state: StateViews

    private val subjectId: String get() = requireArguments().getString("subjectId").orEmpty()
    private val subjectName: String get() = requireArguments().getString("subjectName").orEmpty()

    private val viewModel: SubjectViewModel by viewModels {
        ContainerViewModelFactory(container) { SubjectViewModel(it.learning, subjectId) }
    }

    private val adapter = BindingAdapter<ResourceCard, ItemResourceBinding>(
        inflate = ItemResourceBinding::inflate,
        identity = { it.resourceId },
    ) { binding, resource, _ ->
        binding.resourceType.text = resource.type.uppercase()
        binding.resourceTitle.text = resource.title

        binding.resourceMeta.text = listOfNotNull(
            resource.durationMinutes?.let {
                binding.root.context.getString(R.string.resource_meta_minutes, it)
            },
            resource.topic,
            if (resource.percentComplete > 0) "${resource.percentComplete}% done" else null,
        ).joinToString(" · ")

        binding.root.setOnClickListener { viewModel.requestDownload(resource.resourceId) }
    }

    override fun onCreateView(
        inflater: LayoutInflater,
        parent: ViewGroup?,
        savedInstanceState: Bundle?,
    ): View {
        _binding = FragmentSubjectBinding.inflate(inflater, parent, false)

        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        state = StateViews(binding.stateHost, binding.content)
        state.onRetry { viewModel.load() }

        binding.subjectTitle.text = subjectName
        binding.list.layoutManager = LinearLayoutManager(requireContext())
        binding.list.adapter = adapter

        addTypeChips()

        viewLifecycleOwner.lifecycleScope.launch {
            viewLifecycleOwner.repeatOnLifecycle(Lifecycle.State.STARTED) {
                launch { viewModel.state.collect { current -> state.render(current, adapter::submitList) } }

                launch {
                    viewModel.download.collect { url ->
                        url ?: return@collect

                        // Handed to the system browser: it can resume, it can
                        // write to Downloads, and the signed URL carries its
                        // own authorisation so nothing needs the app's token.
                        startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url)))
                        viewModel.downloadHandled()
                    }
                }
            }
        }

        viewModel.load()
    }

    private fun addTypeChips() {
        val types = listOf(
            null to getString(R.string.filter_all),
            "Video" to getString(R.string.filter_video),
            "Worksheet" to getString(R.string.filter_worksheet),
            "PastPaper" to getString(R.string.filter_past_paper),
            // The API's enum is Worksheet, PastPaper, Video and ReadingList.
            // "Document" is not a member: a filter chip that sent it failed to
            // bind server-side and emptied the list.
            "ReadingList" to getString(R.string.filter_reading),
        )

        types.forEachIndexed { index, (value, label) ->
            val chip = Chip(requireContext()).apply {
                text = label
                isCheckable = true
                isChecked = index == 0
                setOnClickListener { viewModel.filterBy(value) }
            }

            binding.typeChips.addView(chip)
        }
    }

    override fun onDestroyView() {
        super.onDestroyView()
        binding.list.adapter = null
        _binding = null
    }
}
