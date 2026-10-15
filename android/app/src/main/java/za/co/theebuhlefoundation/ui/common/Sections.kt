// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Google (2026) Material Design 3.
// =============================================================================

package za.co.theebuhlefoundation.ui.common

import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import za.co.theebuhlefoundation.databinding.CardSectionBinding
import za.co.theebuhlefoundation.databinding.RowSimpleBinding

/**
 * Builds the card-with-rows block that most screens are made of.
 *
 * These screens are short, read-only lists inside a scrolling column — a
 * RecyclerView per block would buy view recycling that a list of four rows
 * never needs, and cost a nested-scrolling bug on every one of them.
 * The genuinely long lists (resources, messages, notifications) do use
 * RecyclerView.
 */
fun ViewGroup.addSection(
    title: String,
    rows: List<RowContent>,
    onRowClick: ((Int) -> Unit)? = null,
) {
    if (rows.isEmpty()) return

    val inflater = LayoutInflater.from(context)
    val section = CardSectionBinding.inflate(inflater, this, false)

    section.sectionTitle.text = title

    rows.forEachIndexed { index, row ->
        val binding = RowSimpleBinding.inflate(inflater, section.sectionRows, false)

        binding.rowTitle.text = row.title
        binding.rowSubtitle.text = row.subtitle
        binding.rowSubtitle.visibility = if (row.subtitle.isNullOrBlank()) View.GONE else View.VISIBLE

        if (row.pill.isNullOrBlank()) {
            binding.rowPill.visibility = View.GONE
        } else {
            binding.rowPill.text = row.pill
            row.pillColour?.let { binding.rowPill.setTextColor(it) }
        }

        onRowClick?.let { click -> binding.root.setOnClickListener { click(index) } }

        section.sectionRows.addView(binding.root)
    }

    addView(section.root)
}

data class RowContent(
    val title: String,
    val subtitle: String? = null,
    val pill: String? = null,
    val pillColour: Int? = null,
)
